using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Timeline;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Analytics;

/// <param name="Open">Open now (status New, Assigned, In Progress, Need Info, On Hold, Disputed, Reopened).</param>
/// <param name="OpenWeekAgo">Open seven days ago (from the status history) – the trend.</param>
/// <param name="AwaitingRetest">Deployments waiting for a retest.</param>
/// <param name="ClosedThisWeek">Closed since Monday (IST).</param>
public sealed record OverviewKpis(int Open, int OpenWeekAgo, int AwaitingRetest, int Overdue, int CriticalOpen, int ClosedThisWeek);

public sealed record DailyFlow(DateOnly Date, int Created, int Closed);

public sealed record ProjectCount(int ProjectId, string ProjectKey, string ProjectName, int Count);

public sealed record RecentActivityDto(
    int Id,
    ActivityKind Kind,
    string IssueKey,
    string IssueTitle,
    string? UserName,
    string? UserAvatarColor,
    string Summary,
    DateTimeOffset CreatedAt);

/// <summary>Home page (plan 9.1).</summary>
public sealed record OverviewDto(
    DateOnly From,
    DateOnly To,
    OverviewKpis Kpis,
    IReadOnlyList<StatusGroupCount> StatusGroups,
    IReadOnlyList<SeverityCount> OpenBySeverity,
    IReadOnlyList<DailyFlow> CreatedVsClosed,
    IReadOnlyList<ProjectCount> OpenByProject,
    IReadOnlyList<RecentActivityDto> RecentActivity);

public sealed class OverviewHandler(IValidator<AnalyticsFilter> validator, IApplicationDbContext db, IClock clock)
{
    public const int TopProjects = 10;
    public const int RecentActivityCount = 20;

    public async Task<Result<OverviewDto>> HandleAsync(AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(filter, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var range = AnalyticsRange.Resolve(filter, now);
        var issues = Analytics.Scope(db, filter);

        var weekAgo = now.AddDays(-7);
        var openNames = Analytics.OpenStatusNames.ToList();
        var openWeekAgo = await issues
            .Where(i => i.CreatedAt <= weekAgo)
            .Select(i => i.IssueActivities
                .Where(a => a.Kind == ActivityKind.StatusChanged && a.CreatedAt <= weekAgo)
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Select(a => a.NewValue)
                .FirstOrDefault())
            .CountAsync(status => status == null || openNames.Contains(status), cancellationToken);

        var monday = AnalyticsRange.StartOf(AnalyticsRange.WeekOf(WorkingCalendar.IstDate(now)));
        var kpis = new OverviewKpis(
            await issues.Open().CountAsync(cancellationToken),
            openWeekAgo,
            await issues.SelectMany(i => i.Deployments).CountAsync(d => d.RetestResult == RetestResult.Pending, cancellationToken),
            await issues.Overdue(now).CountAsync(cancellationToken),
            await issues.Open().CountAsync(i => i.Severity == Domain.Enums.Severity.Critical, cancellationToken),
            await issues.CountAsync(i => i.Status == IssueStatus.Closed && i.ClosedAt >= monday, cancellationToken));

        var created = await issues
            .Where(i => i.CreatedAt >= range.Start && i.CreatedAt < range.End)
            .Select(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
        var closed = await issues
            .Where(i => i.ClosedAt >= range.Start && i.ClosedAt < range.End)
            .Select(i => i.ClosedAt!.Value)
            .ToListAsync(cancellationToken);
        var createdPerDay = created.CountBy(WorkingCalendar.IstDate).ToDictionary();
        var closedPerDay = closed.CountBy(WorkingCalendar.IstDate).ToDictionary();
        var flow = range.Days().Select(d => new DailyFlow(d, createdPerDay.GetValueOrDefault(d), closedPerDay.GetValueOrDefault(d))).ToList();

        var openByProject = await issues.Open()
            .GroupBy(i => new { i.ProjectId, i.Project.ProjectKey, i.Project.Name })
            .Select(g => new { g.Key.ProjectId, g.Key.ProjectKey, g.Key.Name, Count = g.Count() })
            .OrderByDescending(p => p.Count).ThenBy(p => p.ProjectKey)
            .Take(TopProjects)
            .Select(p => new ProjectCount(p.ProjectId, p.ProjectKey, p.Name, p.Count))
            .ToListAsync(cancellationToken);

        return new OverviewDto(
            range.From,
            range.To,
            kpis,
            await Analytics.StatusGroupsAsync(issues, cancellationToken),
            await Analytics.BySeverityAsync(issues.Open(), cancellationToken),
            flow,
            openByProject,
            await RecentActivityAsync(issues, range, cancellationToken));
    }

    private async Task<IReadOnlyList<RecentActivityDto>> RecentActivityAsync(IQueryable<Domain.Entities.Issue> issues, AnalyticsRange range, CancellationToken cancellationToken)
    {
        var rows = await issues
            .SelectMany(i => i.IssueActivities)
            .Where(a => a.Kind != ActivityKind.SprintEvent && a.CreatedAt >= range.Start && a.CreatedAt < range.End)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(RecentActivityCount)
            .Select(a => new
            {
                a.Id,
                a.Kind,
                a.Issue.IssueKey,
                a.Issue.Title,
                a.UserId,
                UserName = a.User != null ? a.User.FullName : null,
                AvatarColor = a.User != null ? a.User.AvatarColor : null,
                a.Body,
                a.OldValue,
                a.NewValue,
                Environment = a.Environment != null ? a.Environment.Name : null,
                a.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var userIds = rows.Where(r => r.Kind == ActivityKind.Assigned)
            .SelectMany(r => new[] { r.OldValue, r.NewValue })
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return rows.Select(r =>
        {
            var actor = r.UserName ?? "System";
            var summary = r.Kind == ActivityKind.Comment
                ? $"{actor} commented"
                : TimelineSummary.For(r.Kind, actor, r.UserId, r.Body, r.OldValue, r.NewValue, r.Environment, names) ?? actor;
            return new RecentActivityDto(r.Id, r.Kind, r.IssueKey, r.Title, r.UserName, r.AvatarColor, summary, r.CreatedAt);
        }).ToList();
    }
}
