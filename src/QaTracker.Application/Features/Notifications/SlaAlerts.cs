using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Notifications;

public sealed record SlaAlertsResult(int Overdue, int CriticalUnassigned);

/// <summary>
/// Hourly SLA alerts (plan 8.3, ADR-038), only for issues developers act on (sprint in Testing Completed, issue waiting
/// for a fix):
/// <list type="bullet">
/// <item><b>Overdue</b> – in-app for the assignee once the due date has passed; once per issue and due date (a new due
/// date that passes alerts again). Mailed in the daily summary (S19), not instantly.</item>
/// <item><b>Critical unassigned</b> – for every active project member when a critical issue has had no developer for
/// more than <see cref="CriticalUnassignedAfter"/> since testing completed or its last assignment change; once per
/// issue. Mailed instantly.</item>
/// </list>
/// The rows are saved like any other notification, so the change feed mails them and pushes them live.
/// </summary>
public sealed class SlaAlertsHandler(IApplicationDbContext db, IClock clock)
{
    public const string JobName = "sla-alerts";

    public static readonly TimeSpan CriticalUnassignedAfter = TimeSpan.FromHours(2);

    public async Task<SlaAlertsResult> HandleAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var awaitingFix = IssueStates.AwaitingFix.ToList();
        var open = db.Issues.AsNoTracking().Where(i => awaitingFix.Contains(i.Status) && i.Sprint.Status == SprintStatus.TestingCompleted);

        var overdue = await open
            .Where(i => i.AssigneeId != null && i.Assignee!.IsActive && i.DueDate != null && i.DueDate < now)
            .Where(i => !i.Notifications.Any(n => n.Type == NotificationType.Overdue && n.UserId == i.AssigneeId && n.CreatedAt >= i.DueDate))
            .Select(i => new { i.Id, i.IssueKey, i.Title, AssigneeId = i.AssigneeId!.Value, DueDate = i.DueDate!.Value })
            .ToListAsync(cancellationToken);
        foreach (var issue in overdue)
        {
            db.Notifications.Add(Create(issue.AssigneeId, NotificationType.Overdue, issue.Id,
                $"{issue.IssueKey} is overdue",
                $"{issue.Title} – was due {FormatDue(issue.DueDate)}.",
                now));
        }

        var cutoff = now - CriticalUnassignedAfter;
        var critical = await open
            .Where(i => i.Severity == Severity.Critical && i.AssigneeId == null && i.Sprint.TestingCompletedAt <= cutoff)
            .Where(i => !i.IssueActivities.Any(a => a.Kind == ActivityKind.Assigned && a.CreatedAt > cutoff))
            .Where(i => !i.Notifications.Any(n => n.Type == NotificationType.CriticalUnassigned))
            .Select(i => new { i.Id, i.IssueKey, i.Title, i.ProjectId })
            .ToListAsync(cancellationToken);
        var projectIds = critical.Select(i => i.ProjectId).Distinct().ToList();
        var members = (await db.ProjectMembers.AsNoTracking()
                .Where(m => projectIds.Contains(m.ProjectId) && m.User.IsActive)
                .Select(m => new { m.ProjectId, m.UserId })
                .ToListAsync(cancellationToken))
            .ToLookup(m => m.ProjectId, m => m.UserId);
        var criticalNotifications = 0;
        foreach (var issue in critical)
        {
            foreach (var userId in members[issue.ProjectId])
            {
                db.Notifications.Add(Create(userId, NotificationType.CriticalUnassigned, issue.Id,
                    $"Critical {issue.IssueKey} has no developer",
                    $"{issue.Title} – unassigned for more than {CriticalUnassignedAfter.TotalHours:0} hours. Pick it from My work or assign it.",
                    now));
                criticalNotifications++;
            }
        }

        var run = await db.JobRuns.SingleOrDefaultAsync(j => j.Name == JobName, cancellationToken);
        if (run is null)
        {
            run = new JobRun { Name = JobName };
            db.JobRuns.Add(run);
        }

        run.LastRunAt = now;
        run.LastResult = string.Create(CultureInfo.InvariantCulture,
            $"{overdue.Count} overdue, {critical.Count} critical unassigned ({criticalNotifications} notifications)");
        await db.SaveChangesAsync(cancellationToken);
        return new SlaAlertsResult(overdue.Count, critical.Count);
    }

    private static string FormatDue(DateTimeOffset due) =>
        due.ToOffset(WorkingCalendar.Ist).ToString("dd'/'MMM'/'yyyy HH:mm", CultureInfo.InvariantCulture);

    private static Notification Create(int userId, NotificationType type, int issueId, string title, string body, DateTimeOffset now) =>
        new()
        {
            UserId = userId,
            Type = type,
            IssueId = issueId,
            Title = title.Length <= 200 ? title : string.Concat(title.AsSpan(0, 199), "…"),
            Body = body.Length <= 1000 ? body : string.Concat(body.AsSpan(0, 999), "…"),
            IsRead = false,
            CreatedAt = now,
        };
}
