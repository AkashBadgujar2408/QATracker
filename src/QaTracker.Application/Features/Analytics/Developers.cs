using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Analytics;

public sealed record WorkloadDto(int UserId, string Name, string? AvatarColor, int Assigned, int InProgress, int NeedInfo, int OnHold)
{
    public int Total => Assigned + InProgress + NeedInfo + OnHold;
}

/// <param name="Rate">On-time fixes / all fixes (0–1); 0 when nothing was fixed.</param>
public sealed record SlaComplianceDto(int OnTime, int Total, double Rate);

/// <summary>The signed-in user's own numbers ("My stats", plan 9.3).</summary>
public sealed record MyStatsDto(int Open, int Overdue, int FixedThisWeek, double? AverageFixHours);

/// <summary>Developer dashboard (plan 9.3). Fixes = the first change of each issue to Fixed, by whoever made it.</summary>
public sealed record DevelopersDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<WorkloadDto> Workload,
    WeeklySeries<int> FixedPerWeek,
    IReadOnlyList<UserMetric> AverageFixHours,
    IReadOnlyList<UserMetric> FirstTimeRight,
    SlaComplianceDto Sla,
    MyStatsDto Me);

public sealed class DevelopersHandler(IValidator<AnalyticsFilter> validator, IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<Result<DevelopersDto>> HandleAsync(AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(filter, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var range = AnalyticsRange.Resolve(filter, now);
        var holidays = await Analytics.HolidaysAsync(db, cancellationToken);
        var issues = Analytics.Scope(db, filter);

        var workloadStatuses = new[] { IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.NeedInfo, IssueStatus.OnHold, IssueStatus.Reopened };
        var workloadRows = await issues
            .Where(i => i.AssigneeId != null && workloadStatuses.Contains(i.Status))
            .GroupBy(i => new { i.AssigneeId, i.Status })
            .Select(g => new { UserId = g.Key.AssigneeId!.Value, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Every first fix in scope (all time – My stats and the range both need it), with what fix time and SLA need.
        var fixes = (await issues
                .Select(i => new
                {
                    i.Id,
                    i.ReopenCount,
                    i.DueDate,
                    i.AssigneeId,
                    TestingCompletedAt = i.Sprint.TestingCompletedAt,
                    Fix = i.IssueActivities
                        .Where(a => a.Kind == ActivityKind.StatusChanged && a.NewValue == nameof(IssueStatus.Fixed))
                        .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
                        .Select(a => new { a.UserId, a.CreatedAt })
                        .FirstOrDefault(),
                    FirstAssignedAt = i.IssueActivities
                        .Where(a => a.Kind == ActivityKind.Assigned && a.NewValue != null)
                        .Min(a => (DateTimeOffset?)a.CreatedAt),
                })
                .Where(x => x.Fix != null)
                .ToListAsync(cancellationToken))
            .Where(x => x.Fix!.UserId != null)
            .Select(x =>
            {
                var start = Later(x.TestingCompletedAt, x.FirstAssignedAt);
                return new Fix(
                    x.Fix!.UserId!.Value,
                    x.Fix.CreatedAt,
                    start is { } s ? WorkingCalendar.WorkingHoursBetween(s, x.Fix.CreatedAt, holidays) : null,
                    x.ReopenCount > 0,
                    x.DueDate is { } due ? x.Fix.CreatedAt <= due : null);
            })
            .ToList();

        var inRange = fixes.Where(f => f.At >= range.Start && f.At < range.End).ToList();
        var userIds = workloadRows.Select(w => w.UserId).Concat(inRange.Select(f => f.UserId)).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.AvatarColor })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var workload = workloadRows.GroupBy(w => w.UserId)
            .Select(g =>
            {
                int Count(params IssueStatus[] statuses) => g.Where(w => statuses.Contains(w.Status)).Sum(w => w.Count);
                var user = users[g.Key];
                return new WorkloadDto(g.Key, user.FullName, user.AvatarColor,
                    Count(IssueStatus.Assigned, IssueStatus.Reopened), Count(IssueStatus.InProgress), Count(IssueStatus.NeedInfo), Count(IssueStatus.OnHold));
            })
            .OrderByDescending(w => w.Total).ThenBy(w => w.Name)
            .ToList();

        var weeks = range.Weeks();
        var fixedPerWeek = new WeeklySeries<int>(
            weeks,
            inRange.GroupBy(f => f.UserId)
                .Select(g =>
                {
                    var perWeek = g.CountBy(f => AnalyticsRange.WeekOf(WorkingCalendar.IstDate(f.At))).ToDictionary();
                    return new WeeklySeriesLine<int>(g.Key, users[g.Key].FullName, weeks.Select(w => perWeek.GetValueOrDefault(w)).ToList());
                })
                .OrderByDescending(l => l.Values.Sum()).ThenBy(l => l.Name)
                .ToList());

        var averageFixHours = inRange.Where(f => f.Hours is not null).GroupBy(f => f.UserId)
            .Select(g => new UserMetric(g.Key, users[g.Key].FullName, users[g.Key].AvatarColor, Analytics.Round(g.Average(f => f.Hours!.Value)), g.Count()))
            .OrderBy(m => m.Value).ThenBy(m => m.Name)
            .ToList();

        var firstTimeRight = inRange.GroupBy(f => f.UserId)
            .Select(g => new UserMetric(g.Key, users[g.Key].FullName, users[g.Key].AvatarColor, Analytics.Round(1 - (double)g.Count(f => f.Reopened) / g.Count(), 3), g.Count()))
            .OrderByDescending(m => m.Value).ThenBy(m => m.Name)
            .ToList();

        var withDue = inRange.Where(f => f.OnTime is not null).ToList();
        var onTime = withDue.Count(f => f.OnTime == true);
        var sla = new SlaComplianceDto(onTime, withDue.Count, withDue.Count == 0 ? 0 : Analytics.Round((double)onTime / withDue.Count, 3));

        var me = currentUser.UserId;
        var monday = AnalyticsRange.StartOf(AnalyticsRange.WeekOf(WorkingCalendar.IstDate(now)));
        var myFixes = fixes.Where(f => f.UserId == me && f.Hours is not null).ToList();
        var myStats = new MyStatsDto(
            await issues.Open().CountAsync(i => i.AssigneeId == me, cancellationToken),
            await issues.Overdue(now).CountAsync(i => i.AssigneeId == me, cancellationToken),
            fixes.Count(f => f.UserId == me && f.At >= monday),
            myFixes.Count == 0 ? null : Analytics.Round(myFixes.Average(f => f.Hours!.Value)));

        return new DevelopersDto(range.From, range.To, workload, fixedPerWeek, averageFixHours, firstTimeRight, sla, myStats);
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;

    /// <param name="Hours">Working hours from max(testing completed, first assigned) to the fix.</param>
    /// <param name="OnTime">Fixed on or before the due date; null without a due date.</param>
    private sealed record Fix(int UserId, DateTimeOffset At, double? Hours, bool Reopened, bool? OnTime);
}
