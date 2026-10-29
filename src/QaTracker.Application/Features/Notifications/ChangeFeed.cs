using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Notifications;

/// <summary>
/// Turns what a save writes into notifications (plan 8.3) and live updates (ADR-028). Called by the persistence
/// interceptor for every SaveChanges: <see cref="PrepareAsync"/> before the save adds the Notifications rows (same
/// transaction), <see cref="TakeEvents"/> after the commit gives the SignalR events. Handlers do not call it.
/// <para>Rules come from the timeline entries, so every workflow path (assign, pick, transitions, fixes, retests,
/// deploy notices, comments, mentions) notifies the same way. The actor is never notified; one save gives each user
/// at most one notification per issue (the most important one).</para>
/// </summary>
public sealed class ChangeFeed(IClock clock, NotificationMailer mailer)
{
    private const int TitleMax = 200;
    private const int BodyMax = 1000;
    private const int ExcerptMax = 200;

    /// <summary>Most important first: a user gets only the first of these per issue and save.</summary>
    private static readonly NotificationType[] Importance =
    [
        NotificationType.Reopened, NotificationType.NeedInfo, NotificationType.Disputed, NotificationType.Assigned,
        NotificationType.Fixed, NotificationType.Mentioned, NotificationType.Comment,
    ];

    private readonly List<Pending> _pending = [];

    public bool HasPending => _pending.Count > 0;

    /// <summary>Inspects the tracked changes (DetectChanges already ran) and adds notification rows.</summary>
    public async Task PrepareAsync(IApplicationDbContext db, IReadOnlyList<EntityEntry> entries, CancellationToken cancellationToken)
    {
        var changed = entries.Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var pending = new Pending();
        pending.Issues.AddRange(changed.Where(e => e.Entity is Issue).Select(e => ((Issue)e.Entity, e.State == EntityState.Deleted)));
        pending.Sprints.AddRange(changed.Where(e => e.Entity is TestingSprint).Select(e => (TestingSprint)e.Entity));
        pending.Activities.AddRange(changed.Where(e => e.Entity is IssueActivity && e.State != EntityState.Deleted).Select(e => (IssueActivity)e.Entity));
        var addedActivities = changed.Where(e => e.Entity is IssueActivity && e.State == EntityState.Added).Select(e => (IssueActivity)e.Entity).ToList();
        var addedMentions = changed.Where(e => e.Entity is Mention && e.State == EntityState.Added).Select(e => (Mention)e.Entity).ToList();
        var handlerNotifications = changed.Where(e => e.Entity is Notification && e.State == EntityState.Added).Select(e => (Notification)e.Entity).ToList();

        // Issue facts for the rules and for events of issues changed in SQL (pick) – new issues have no id yet.
        var issueIds = pending.Activities.Select(a => a.IssueId)
            .Concat(addedMentions.Select(m => m.Activity?.IssueId ?? 0))
            .Concat(handlerNotifications.Select(n => n.IssueId ?? 0))
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        var issues = issueIds.Count == 0
            ? []
            : await db.Issues.AsNoTracking()
                .Where(i => issueIds.Contains(i.Id))
                .Select(i => new IssueFacts(i.Id, i.IssueKey, i.Title, i.ProjectId, i.SprintId, i.Sprint.Status, i.ReporterId, i.AssigneeId))
                .ToDictionaryAsync(i => i.Id, cancellationToken);
        pending.IssueFacts = issues;

        var sprintIds = handlerNotifications.Where(n => n.SprintId is not null).Select(n => n.SprintId!.Value).Distinct().ToList();
        var sprintProjects = sprintIds.Count == 0
            ? []
            : await db.TestingSprints.AsNoTracking().Where(s => sprintIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.ProjectId, cancellationToken);
        foreach (var notification in handlerNotifications)
        {
            var facts = notification.IssueId is { } id ? issues.GetValueOrDefault(id) : null;
            pending.Notifications.Add(new CreatedNotification(
                notification, facts?.Key, facts?.ProjectId ?? (notification.SprintId is { } s ? sprintProjects.GetValueOrDefault(s) : null)));
        }

        var planned = await PlanAsync(db, issues, addedActivities, addedMentions, cancellationToken);
        foreach (var item in planned)
        {
            db.Notifications.Add(item.Notification);
            pending.Notifications.Add(item);
        }

        // Instant e-mails go into the outbox in the same save (ADR-029).
        await mailer.QueueAsync(db, pending.Notifications.Select(n => (n.Notification, n.IssueKey, n.ProjectId)).ToList(), cancellationToken);

        _pending.Add(pending);
    }

    /// <summary>Events of the saves since the last call (after the commit: ids are set).</summary>
    public IReadOnlyList<RealtimeEvent> TakeEvents()
    {
        var events = new List<RealtimeEvent>();
        var issueKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pending in _pending)
        {
            foreach (var (issue, deleted) in pending.Issues)
            {
                if (issueKeys.Add(issue.IssueKey))
                {
                    events.Add(new IssueChangedEvent(issue.IssueKey, issue.ProjectId, issue.SprintId, deleted));
                }
            }

            foreach (var activity in pending.Activities)
            {
                var facts = pending.IssueFacts.GetValueOrDefault(activity.IssueId);
                var key = facts?.Key ?? activity.Issue?.IssueKey;
                if (key is null)
                {
                    continue;
                }

                events.Add(new ActivityAddedEvent(key, activity.Id, activity.Kind));
                if (facts is not null && issueKeys.Add(key))
                {
                    events.Add(new IssueChangedEvent(key, facts.ProjectId, facts.SprintId, Deleted: false));
                }
            }

            events.AddRange(pending.Sprints.Select(s => new SprintChangedEvent(s.Id, s.ProjectId, s.Status)));
            events.AddRange(pending.Notifications.Select(n => new NotificationCreatedEvent(
                n.Notification.UserId,
                new NotificationDto(n.Notification.Id, n.Notification.Type, n.Notification.Title, n.Notification.Body, n.IssueKey,
                    n.Notification.SprintId, n.ProjectId, n.Notification.IsRead, n.Notification.CreatedAt))));
        }

        _pending.Clear();
        return events;
    }

    /// <summary>The save failed or its transaction rolled back: nothing happened.</summary>
    public void Discard() => _pending.Clear();

    private async Task<List<CreatedNotification>> PlanAsync(
        IApplicationDbContext db,
        Dictionary<int, IssueFacts> issues,
        List<IssueActivity> activities,
        List<Mention> mentions,
        CancellationToken cancellationToken)
    {
        var wanted = new List<Wanted>();
        var notices = new List<(IssueActivity Activity, IssueFacts Issue)>();
        var commentIssues = new List<(IssueActivity Activity, IssueFacts Issue)>();

        foreach (var activity in activities)
        {
            if (!issues.TryGetValue(activity.IssueId, out var issue))
            {
                continue; // new issue (create): nobody to tell yet – pre-assignees hear it when testing completes
            }

            var actor = activity.UserId;
            switch (activity.Kind)
            {
                case ActivityKind.Assigned when int.TryParse(activity.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var to)
                                                && issue.SprintStatus != SprintStatus.Testing:
                    wanted.Add(new Wanted(to, actor, issue, NotificationType.Assigned, activity));
                    break;
                case ActivityKind.StatusChanged when activity.NewValue == nameof(IssueStatus.Reopened) && issue.AssigneeId is { } assignee:
                    wanted.Add(new Wanted(assignee, actor, issue, NotificationType.Reopened, activity));
                    break;
                case ActivityKind.StatusChanged when activity.NewValue == nameof(IssueStatus.NeedInfo):
                    wanted.Add(new Wanted(issue.ReporterId, actor, issue, NotificationType.NeedInfo, activity));
                    break;
                case ActivityKind.StatusChanged when activity.NewValue == nameof(IssueStatus.Disputed):
                    wanted.Add(new Wanted(issue.ReporterId, actor, issue, NotificationType.Disputed, activity));
                    break;
                case ActivityKind.Deployed when activity.Deployment?.DeployNotice is not null:
                    notices.Add((activity, issue));
                    break;
                case ActivityKind.Deployed:
                    wanted.Add(new Wanted(issue.ReporterId, actor, issue, NotificationType.Fixed, activity));
                    break;
                case ActivityKind.Comment:
                    commentIssues.Add((activity, issue));
                    break;
            }
        }

        foreach (var mention in mentions)
        {
            var activity = mention.Activity;
            if (activity is not null && issues.TryGetValue(activity.IssueId, out var issue))
            {
                wanted.Add(new Wanted(mention.UserId, activity.UserId, issue, NotificationType.Mentioned, activity));
            }
        }

        if (commentIssues.Count > 0)
        {
            var ids = commentIssues.Select(c => c.Issue.Id).Distinct().ToList();
            var watchers = (await db.IssueWatchers.AsNoTracking().Where(w => ids.Contains(w.IssueId)).Select(w => new { w.IssueId, w.UserId }).ToListAsync(cancellationToken))
                .ToLookup(w => w.IssueId, w => w.UserId);
            foreach (var (activity, issue) in commentIssues)
            {
                var audience = watchers[issue.Id].Append(issue.ReporterId);
                if (issue.AssigneeId is { } assignee)
                {
                    audience = audience.Append(assignee);
                }

                wanted.AddRange(audience.Distinct().Select(user => new Wanted(user, activity.UserId, issue, NotificationType.Comment, activity)));
            }
        }

        var userIds = wanted.Select(w => w.UserId).Concat(wanted.Where(w => w.ActorId is not null).Select(w => w.ActorId!.Value))
            .Concat(notices.Where(n => n.Activity.UserId is not null).Select(n => n.Activity.UserId!.Value))
            .Concat(notices.Select(n => n.Issue.ReporterId))
            .Distinct()
            .ToList();
        var users = userIds.Count == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new { u.FullName, u.IsActive }, cancellationToken);
        string Name(int? id) => id is { } value && users.TryGetValue(value, out var user) ? user.FullName : "Someone";
        bool Reachable(int id) => users.TryGetValue(id, out var user) && user.IsActive;

        var now = clock.UtcNow;
        var created = new List<CreatedNotification>();
        foreach (var group in wanted.Where(w => w.UserId != w.ActorId && Reachable(w.UserId)).GroupBy(w => (w.UserId, w.Issue.Id)))
        {
            var chosen = group.OrderBy(w => Array.IndexOf(Importance, w.Type)).First();
            // Need info: the status change has no text – the question is the comment saved with it.
            var detail = chosen.Activity.Body ?? group.Select(w => w.Activity.Body).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));
            var (title, body) = Describe(chosen, Name(chosen.ActorId), detail);
            created.Add(Create(chosen.UserId, chosen.Type, chosen.Issue, title, body, now));
        }

        // Deploy notice: one notification per reporter and notice (plan 8.3 "one per batch").
        foreach (var batch in notices.GroupBy(n => (Notice: n.Activity.Deployment!.DeployNotice!, n.Issue.ReporterId)))
        {
            var (notice, reporter) = batch.Key;
            var actor = batch.First().Activity.UserId;
            if (reporter == actor || !Reachable(reporter))
            {
                continue;
            }

            var keys = batch.Select(n => n.Issue.Key).Distinct().ToList();
            var single = keys.Count == 1 ? batch.First().Issue : null;
            var environment = await db.ProjectEnvironments.AsNoTracking().Where(e => e.Id == notice.EnvironmentId).Select(e => e.Name).SingleOrDefaultAsync(cancellationToken) ?? "an environment";
            var deployedVersion = batch.First().Activity.NewValue;
            var version = string.IsNullOrWhiteSpace(deployedVersion) ? string.Empty : $" (version {deployedVersion})";
            var title = keys.Count == 1
                ? $"{keys[0]} deployed on {environment} – ready to retest"
                : $"{keys.Count} fixes deployed on {environment} – ready to retest";
            var body = $"{Name(actor)}{version}: {string.Join(", ", keys)}. {notice.Note}";
            var notification = Create(reporter, NotificationType.Fixed, single, title, body, now, batch.First().Issue.ProjectId);
            notification.Notification.SprintId = batch.First().Issue.SprintId; // keeps the project when no single issue is linked
            created.Add(notification);
        }

        return created;
    }

    private static (string Title, string Body) Describe(Wanted w, string actor, string? text)
    {
        var key = w.Issue.Key;
        var title = w.Type switch
        {
            NotificationType.Assigned => $"{actor} assigned {key} to you",
            NotificationType.Reopened => $"{key} was reopened by {actor}",
            NotificationType.NeedInfo => $"{actor} needs information on {key}",
            NotificationType.Disputed => $"{actor} disputed {key}",
            NotificationType.Fixed => $"{key} fixed by {actor} – ready to retest",
            NotificationType.Mentioned => $"{actor} mentioned you on {key}",
            _ => $"{actor} commented on {key}",
        };
        var detail = Excerpt(text); // comment, question, reason or assignment note
        return (title, detail is null ? w.Issue.Title : $"{w.Issue.Title} – {detail}");
    }

    private static CreatedNotification Create(int userId, NotificationType type, IssueFacts? issue, string title, string body, DateTimeOffset now, int? projectId = null) =>
        new(
            new Notification
            {
                UserId = userId,
                Type = type,
                IssueId = issue?.Id,
                Title = Truncate(title, TitleMax),
                Body = Truncate(body, BodyMax),
                IsRead = false,
                CreatedAt = now,
            },
            issue?.Key,
            issue?.ProjectId ?? projectId);

    private static string? Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Truncate(flat, ExcerptMax);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    private sealed record IssueFacts(int Id, string Key, string Title, int ProjectId, int SprintId, SprintStatus SprintStatus, int ReporterId, int? AssigneeId);

    private sealed record Wanted(int UserId, int? ActorId, IssueFacts Issue, NotificationType Type, IssueActivity Activity);

    private sealed record CreatedNotification(Notification Notification, string? IssueKey, int? ProjectId);

    private sealed class Pending
    {
        public List<(Issue Issue, bool Deleted)> Issues { get; } = [];

        public List<TestingSprint> Sprints { get; } = [];

        public List<IssueActivity> Activities { get; } = [];

        public List<CreatedNotification> Notifications { get; } = [];

        public Dictionary<int, IssueFacts> IssueFacts { get; set; } = [];
    }
}
