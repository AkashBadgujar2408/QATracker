using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Attachments;
using QaTracker.Application.Features.Issues;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Timeline;

public sealed record MentionDto(int UserId, string UserName, string FullName);

/// <summary>
/// One timeline entry (plan 7.5): a comment or an immutable system event. <paramref name="Summary"/> is the ready-made
/// line ("Akash assigned to Nitin"); the raw values stay available for richer rendering.
/// </summary>
public sealed record ActivityDto(
    int Id,
    ActivityKind Kind,
    int? UserId,
    string? UserName,
    string? AvatarColor,
    string? Summary,
    string? Body,
    string? OldValue,
    string? NewValue,
    int? EnvironmentId,
    string? EnvironmentName,
    int? DeploymentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool CanEdit,
    IReadOnlyList<MentionDto> Mentions,
    IReadOnlyList<AttachmentDto> Attachments);

/// <param name="HasMore">Older entries exist – ask again with <c>before</c> = the first item's id.</param>
public sealed record TimelinePage(IReadOnlyList<ActivityDto> Items, bool HasMore);

public enum TimelineFilter
{
    All,
    Comments,
    History,
}

public sealed record ListActivitiesQuery(string Key, int? Before, int Size, TimelineFilter Filter)
{
    public const int DefaultSize = 50;
    public const int MaxSize = 200;
}

public sealed record CreateCommentCommand(string Key, string? Body, IReadOnlyList<IncomingFile> Files);

public sealed record EditCommentCommand(int Id, string? Body);

public static class CommentRules
{
    public const int BodyMaxLength = 4000;

    /// <summary>Authors may edit their comment this long (plan 7.5).</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(10);

    public static bool CanEdit(int? authorId, int userId, DateTimeOffset createdAt, DateTimeOffset now) =>
        authorId == userId && now - createdAt <= EditWindow;
}

/// <summary>"@nitin.dev" mentions – user names (ADR-011) after an @ that starts the text or follows a space/bracket.</summary>
public static partial class MentionParser
{
    [GeneratedRegex(@"(?<![\w.@-])@([A-Za-z0-9._-]{3,50})", RegexOptions.CultureInvariant)]
    private static partial Regex MentionPattern();

    public static IReadOnlyList<string> UserNames(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : MentionPattern().Matches(text).Select(m => m.Groups[1].Value.TrimEnd('.', '-', '_')).Where(n => n.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>Watchers are added automatically: reporter, assignee and everyone who comments (plan S14).</summary>
public static class Watchers
{
    public static async Task AddAsync(IApplicationDbContext db, int issueId, IEnumerable<int?> userIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var wanted = userIds.OfType<int>().Distinct().ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var existing = await db.IssueWatchers.Where(w => w.IssueId == issueId && wanted.Contains(w.UserId)).Select(w => w.UserId).ToListAsync(cancellationToken);
        var pending = db.IssueWatchers.Local.Where(w => w.IssueId == issueId).Select(w => w.UserId);
        foreach (var userId in wanted.Except(existing).Except(pending).ToList())
        {
            db.IssueWatchers.Add(new IssueWatcher { IssueId = issueId, UserId = userId, AddedAt = now });
        }
    }
}

public sealed class CreateCommentValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.Body).MaximumLength(CommentRules.BodyMaxLength);
        RuleFor(x => x.Body).NotEmpty().When(x => x.Files.Count == 0).WithMessage("Write a comment or attach a file.");
    }
}

public sealed class EditCommentValidator : AbstractValidator<EditCommentCommand>
{
    public EditCommentValidator()
    {
        RuleFor(x => x.Body).NotEmpty().WithMessage("A comment cannot be empty.").MaximumLength(CommentRules.BodyMaxLength);
    }
}

/// <summary>Builds timeline entries with user names, summaries, mentions and attachments.</summary>
public sealed class TimelineReader(IApplicationDbContext db, IAttachmentUrlSigner signer, ICurrentUser currentUser, IClock clock)
{
    public async Task<List<ActivityDto>> ReadAsync(IQueryable<IssueActivity> activities, CancellationToken cancellationToken)
    {
        var rows = await activities.AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.IssueId,
                a.Kind,
                a.UserId,
                UserName = a.User != null ? a.User.FullName : null,
                AvatarColor = a.User != null ? a.User.AvatarColor : null,
                a.Body,
                a.OldValue,
                a.NewValue,
                a.EnvironmentId,
                EnvironmentName = a.Environment != null ? a.Environment.Name : null,
                a.DeploymentId,
                a.CreatedAt,
                a.EditedAt,
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var mentions = (await db.Mentions.AsNoTracking()
                .Where(m => ids.Contains(m.ActivityId))
                .Select(m => new { m.ActivityId, Mention = new MentionDto(m.UserId, m.User.UserName, m.User.FullName) })
                .ToListAsync(cancellationToken))
            .ToLookup(m => m.ActivityId, m => m.Mention);

        // Comment files only: per-file Attachment events already name their file.
        var commentIds = rows.Where(r => r.Kind == ActivityKind.Comment).Select(r => r.Id).ToList();
        var files = commentIds.Count == 0
            ? []
            : await AttachmentMapping.ListAsync(db, signer, currentUser.UserId, db.Attachments.Where(a => a.ActivityId != null && commentIds.Contains(a.ActivityId.Value)), cancellationToken);
        var fileActivity = commentIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.Attachments.AsNoTracking().Where(a => a.ActivityId != null && commentIds.Contains(a.ActivityId.Value))
                .ToDictionaryAsync(a => a.Id, a => a.ActivityId!.Value, cancellationToken);
        var filesByActivity = files.ToLookup(f => fileActivity[f.Id]);

        // Assignment events hold user ids.
        var userIds = rows.Where(r => r.Kind == ActivityKind.Assigned)
            .SelectMany(r => new[] { r.OldValue, r.NewValue })
            .Select(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
            .OfType<int>().Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var now = clock.UtcNow;
        return rows.Select(r => new ActivityDto(
                r.Id,
                r.Kind,
                r.UserId,
                r.UserName,
                r.AvatarColor,
                TimelineSummary.For(r.Kind, r.UserName ?? "System", r.UserId, r.Body, r.OldValue, r.NewValue, r.EnvironmentName, names),
                r.Body,
                r.OldValue,
                r.NewValue,
                r.EnvironmentId,
                r.EnvironmentName,
                r.DeploymentId,
                r.CreatedAt,
                r.EditedAt,
                r.Kind == ActivityKind.Comment && CommentRules.CanEdit(r.UserId, currentUser.UserId, r.CreatedAt, now),
                mentions[r.Id].ToList(),
                filesByActivity[r.Id].ToList()))
            .ToList();
    }
}

/// <summary>Plain-English line for a system event (comments have none – their body is the content).</summary>
public static class TimelineSummary
{
    public static string? For(
        ActivityKind kind,
        string actor,
        int? actorId,
        string? body,
        string? oldValue,
        string? newValue,
        string? environment,
        IReadOnlyDictionary<int, string> names)
    {
        string NameOf(string? id) =>
            int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) && names.TryGetValue(userId, out var name) ? name : "someone";

        var on = environment is null ? string.Empty : $" on {environment}";
        return kind switch
        {
            ActivityKind.Created => $"{actor} reported the issue",
            ActivityKind.Comment => null,
            ActivityKind.Assigned when newValue is null => $"{actor} unassigned {NameOf(oldValue)}",
            ActivityKind.Assigned when oldValue is null && newValue == actorId?.ToString(CultureInfo.InvariantCulture) => $"{actor} picked the issue",
            ActivityKind.Assigned when oldValue is null => $"{actor} assigned to {NameOf(newValue)}",
            ActivityKind.Assigned => $"{actor} reassigned from {NameOf(oldValue)} to {NameOf(newValue)}",
            ActivityKind.StatusChanged => StatusLine(actor, oldValue, newValue),
            ActivityKind.FieldChanged => $"{actor} changed {body?.ToLowerInvariant() ?? "a field"}",
            ActivityKind.Deployed => $"{actor} deployed the fix{on}" + (newValue is null ? string.Empty : $" (version {newValue})"),
            ActivityKind.RetestPassed => $"{actor} verified{on}",
            ActivityKind.RetestFailed => $"{actor}: retest failed{on}",
            ActivityKind.Attachment when oldValue is not null => $"{actor} removed {oldValue}",
            ActivityKind.Attachment => $"{actor} attached {body}",
            _ => body,
        };
    }

    private static string StatusLine(string actor, string? from, string? to)
    {
        _ = Enum.TryParse<IssueStatus>(from, out var old);
        if (!Enum.TryParse<IssueStatus>(to, out var target))
        {
            return $"{actor} changed the status";
        }

        return target switch
        {
            IssueStatus.InProgress when old == IssueStatus.NeedInfo => $"{actor} resumed work",
            IssueStatus.InProgress => $"{actor} started work",
            IssueStatus.NeedInfo => $"{actor} asked for information",
            IssueStatus.OnHold => $"{actor} put the issue on hold",
            IssueStatus.Assigned when old == IssueStatus.OnHold => $"{actor} took the issue off hold",
            IssueStatus.Assigned when old == IssueStatus.Disputed => $"{actor} disagreed – back to the developer",
            IssueStatus.Assigned => $"{actor} moved the issue to Assigned",
            IssueStatus.Disputed => $"{actor} disputed the issue (not a bug)",
            IssueStatus.Rejected => $"{actor} rejected the issue",
            IssueStatus.Fixed => $"{actor} marked the issue Fixed",
            IssueStatus.Verified => $"{actor} verified the fix",
            IssueStatus.Closed => $"{actor} closed the issue",
            IssueStatus.Reopened => $"{actor} reopened the issue",
            _ => $"{actor} moved the issue to {target}",
        };
    }
}

/// <summary>The issue's timeline, oldest first; pages go backwards from <c>before</c> (newest page first).</summary>
public sealed class ListActivitiesHandler(IApplicationDbContext db, TimelineReader reader)
{
    public async Task<Result<TimelinePage>> HandleAsync(ListActivitiesQuery query, CancellationToken cancellationToken)
    {
        if (query.Size is < 1 or > ListActivitiesQuery.MaxSize)
        {
            return Error.Validation("size", $"Size must be between 1 and {ListActivitiesQuery.MaxSize}.");
        }

        var key = IssueRules.NormalizeKey(query.Key);
        var issueId = await db.Issues.AsNoTracking().Where(i => i.IssueKey == key).Select(i => (int?)i.Id).SingleOrDefaultAsync(cancellationToken);
        if (issueId is null)
        {
            return Error.NotFound("Issue");
        }

        var activities = db.IssueActivities.Where(a => a.IssueId == issueId);
        activities = query.Filter switch
        {
            TimelineFilter.Comments => activities.Where(a => a.Kind == ActivityKind.Comment),
            TimelineFilter.History => activities.Where(a => a.Kind != ActivityKind.Comment),
            _ => activities,
        };
        if (query.Before is { } before)
        {
            activities = activities.Where(a => a.Id < before);
        }

        // Ids grow with time, so they order the stream (ties in CreatedAt included).
        var pageIds = await activities.OrderByDescending(a => a.Id).Select(a => a.Id).Take(query.Size + 1).ToListAsync(cancellationToken);
        var hasMore = pageIds.Count > query.Size;
        var ids = pageIds.Take(query.Size).ToList();

        var items = await reader.ReadAsync(db.IssueActivities.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Id), cancellationToken);
        return new TimelinePage(items, hasMore);
    }
}

/// <summary>
/// Comment on an issue (any role, any sprint state – plan 4/6): plain text with @mentions of active users, up to five
/// files (screenshots pasted into the composer). The author becomes a watcher.
/// </summary>
public sealed class CreateCommentHandler(
    IValidator<CreateCommentCommand> validator,
    IApplicationDbContext db,
    AttachmentWriter writer,
    TimelineReader reader,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ActivityDto>> HandleAsync(CreateCommentCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (AttachmentWriter.Inspect(command.Files, minFiles: 0, out var files) is { } badFiles)
        {
            return badFiles;
        }

        var key = IssueRules.NormalizeKey(command.Key);
        var issueId = await db.Issues.AsNoTracking().Where(i => i.IssueKey == key).Select(i => (int?)i.Id).SingleOrDefaultAsync(cancellationToken);
        if (issueId is null)
        {
            return Error.NotFound("Issue");
        }

        var now = clock.UtcNow;
        var comment = new IssueActivity
        {
            IssueId = issueId.Value,
            UserId = currentUser.UserId,
            Kind = ActivityKind.Comment,
            Body = string.IsNullOrWhiteSpace(command.Body) ? null : command.Body.Trim(),
            CreatedAt = now,
        };
        db.IssueActivities.Add(comment);
        await CommentMentions.ApplyAsync(db, comment, null, cancellationToken);
        await Watchers.AddAsync(db, issueId.Value, [currentUser.UserId], now, cancellationToken);

        if (files.Count > 0)
        {
            await writer.SaveAsync(issueId.Value, files, _ => comment, now, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return (await reader.ReadAsync(db.IssueActivities.Where(a => a.Id == comment.Id), cancellationToken)).Single();
    }
}

internal static class CommentMentions
{
    /// <summary>Mentions = the active users named in the body; removed names lose their mention on edit.</summary>
    public static async Task ApplyAsync(IApplicationDbContext db, IssueActivity comment, ICollection<Mention>? existing, CancellationToken cancellationToken)
    {
        var names = MentionParser.UserNames(comment.Body);
        var userIds = names.Count == 0
            ? []
            : await db.Users.Where(u => u.IsActive && names.Contains(u.UserName)).Select(u => u.Id).ToListAsync(cancellationToken);
        userIds.Remove(comment.UserId ?? 0);

        var current = existing?.ToList() ?? [];
        foreach (var stale in current.Where(m => !userIds.Contains(m.UserId)))
        {
            db.Mentions.Remove(stale);
        }

        foreach (var userId in userIds.Except(current.Select(m => m.UserId)))
        {
            db.Mentions.Add(new Mention { Activity = comment, UserId = userId });
        }
    }
}

/// <summary>Edit one's own comment within 10 minutes (plan 7.5); system events are never editable.</summary>
public sealed class EditCommentHandler(
    IValidator<EditCommentCommand> validator,
    IApplicationDbContext db,
    TimelineReader reader,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ActivityDto>> HandleAsync(EditCommentCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var comment = await db.IssueActivities.Include(a => a.Mentions).SingleOrDefaultAsync(a => a.Id == command.Id && a.Kind == ActivityKind.Comment, cancellationToken);
        if (comment is null)
        {
            return Error.NotFound("Comment");
        }

        var now = clock.UtcNow;
        if (comment.UserId != currentUser.UserId)
        {
            return Error.Forbidden("not_author", "Only the author can edit a comment.");
        }

        if (!CommentRules.CanEdit(comment.UserId, currentUser.UserId, comment.CreatedAt, now))
        {
            return Error.Rule("edit_window_passed", "Comments can be edited for 10 minutes only. Add a new comment instead.");
        }

        var body = command.Body!.Trim();
        if (body != comment.Body)
        {
            comment.Body = body;
            comment.EditedAt = now;
            await CommentMentions.ApplyAsync(db, comment, comment.Mentions, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return (await reader.ReadAsync(db.IssueActivities.Where(a => a.Id == comment.Id), cancellationToken)).Single();
    }
}

/// <summary>
/// Other open issues on the same screen (or module) of the project (plan 7.3) – so one developer can fix them together.
/// Same screen first, then the rest of the module; newest first.
/// </summary>
public sealed class RelatedIssuesHandler(IApplicationDbContext db)
{
    public const int MaxResults = 20;

    public async Task<Result<IReadOnlyList<RelatedIssueDto>>> HandleAsync(string key, CancellationToken cancellationToken)
    {
        var normalized = IssueRules.NormalizeKey(key);
        var issue = await db.Issues.AsNoTracking().Where(i => i.IssueKey == normalized).Select(i => new { i.Id, i.ProjectId, i.ModuleId, i.ScreenName }).SingleOrDefaultAsync(cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var screen = issue.ScreenName;
        var related = await db.Issues.AsNoTracking()
            .Where(i => i.ProjectId == issue.ProjectId && i.ModuleId == issue.ModuleId && i.Id != issue.Id
                && i.Status != IssueStatus.Closed && i.Status != IssueStatus.Rejected)
            .OrderByDescending(i => screen != null && i.ScreenName == screen)
            .ThenByDescending(i => i.CreatedAt)
            .Take(MaxResults)
            .Select(i => new RelatedIssueDto(i.Id, i.IssueKey, i.Title, i.ScreenName, i.Severity, i.Status, i.Assignee != null ? i.Assignee.FullName : null, screen != null && i.ScreenName == screen))
            .ToListAsync(cancellationToken);
        return related;
    }
}

/// <param name="SameScreen">On the same screen as the issue (otherwise only the same module).</param>
public sealed record RelatedIssueDto(int Id, string Key, string Title, string? ScreenName, Severity Severity, IssueStatus Status, string? AssigneeName, bool SameScreen);
