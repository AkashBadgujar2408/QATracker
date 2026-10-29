using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Analytics;

public sealed record PhaseCount(int PhaseId, string Phase, int Count);

/// <summary>Issues found per sprint phase of a project (Early / UAT / LIVE …) – healthy projects find most early.</summary>
public sealed record LeakageDto(int ProjectId, string ProjectKey, string ProjectName, IReadOnlyList<PhaseCount> ByPhase);

public sealed record TurnaroundByType(EnvironmentType EnvironmentType, double MedianHours, int Count);

/// <summary>Deployed → retested, in working hours (retests done within the range).</summary>
public sealed record RetestTurnaroundDto(double MedianHours, int Count, IReadOnlyList<TurnaroundByType> ByEnvironmentType);

public enum AgeingBucket
{
    UnderOneDay,
    OneToTwoDays,
    ThreeToFiveDays,
    OverFiveDays,
}

public sealed record AgeingCount(AgeingBucket Bucket, string Label, int Count);

public sealed record ModuleHotspot(int ModuleId, string Module, string ProjectKey, int Count, int Open);

public sealed record RejectTypeCount(RejectType Type, int Count);

/// <param name="Ratio">Rejected / all issues (0–1).</param>
public sealed record RejectedDto(int Rejected, int Total, double Ratio, IReadOnlyList<RejectTypeCount> ByType);

/// <summary>Quality and testing dashboard (plan 9.4).</summary>
public sealed record QualityDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<LeakageDto> Leakage,
    RetestTurnaroundDto RetestTurnaround,
    IReadOnlyList<AgeingCount> RetestAgeing,
    IReadOnlyList<ModuleHotspot> ModuleHotspots,
    RejectedDto Rejected,
    WeeklySeries<Severity> SeverityTrend);

public sealed class QualityHandler(IValidator<AnalyticsFilter> validator, IApplicationDbContext db, IClock clock)
{
    public const int TopModules = 30;

    public async Task<Result<QualityDto>> HandleAsync(AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(filter, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var range = AnalyticsRange.Resolve(filter, now);
        var holidays = await Analytics.HolidaysAsync(db, cancellationToken);
        var issues = Analytics.Scope(db, filter);

        var phases = await db.SprintPhases.AsNoTracking().OrderBy(p => p.SortOrder).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        var leakageRows = await issues
            .GroupBy(i => new { i.ProjectId, i.Project.ProjectKey, i.Project.Name, i.Sprint.PhaseId })
            .Select(g => new { g.Key.ProjectId, g.Key.ProjectKey, g.Key.Name, g.Key.PhaseId, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var usedPhases = phases.Where(p => leakageRows.Any(r => r.PhaseId == p.Id)).ToList();
        var leakage = leakageRows.GroupBy(r => new { r.ProjectId, r.ProjectKey, r.Name })
            .Select(g => new LeakageDto(g.Key.ProjectId, g.Key.ProjectKey, g.Key.Name,
                usedPhases.Select(p => new PhaseCount(p.Id, p.Name, g.Where(r => r.PhaseId == p.Id).Sum(r => r.Count))).ToList()))
            .OrderByDescending(l => l.ByPhase.Sum(p => p.Count)).ThenBy(l => l.ProjectKey)
            .ToList();

        var deployments = issues.SelectMany(i => i.Deployments);
        var retests = (await deployments
                .Where(d => d.RetestedAt >= range.Start && d.RetestedAt < range.End && d.RetestResult != RetestResult.Pending)
                .Select(d => new { d.Environment.Type, d.DeployedAt, RetestedAt = d.RetestedAt!.Value })
                .ToListAsync(cancellationToken))
            .Select(r => (r.Type, Hours: WorkingCalendar.WorkingHoursBetween(r.DeployedAt, r.RetestedAt, holidays)))
            .ToList();
        var turnaround = new RetestTurnaroundDto(
            Analytics.Round(Analytics.Median(retests.Select(r => r.Hours).ToList())),
            retests.Count,
            retests.GroupBy(r => r.Type).OrderBy(g => g.Key)
                .Select(g => new TurnaroundByType(g.Key, Analytics.Round(Analytics.Median(g.Select(r => r.Hours).ToList())), g.Count()))
                .ToList());

        var waiting = await deployments.Where(d => d.RetestResult == RetestResult.Pending).Select(d => d.DeployedAt).ToListAsync(cancellationToken);
        var ageing = waiting.CountBy(d => Bucket((now - d).TotalDays)).ToDictionary();

        var open = IssueStates.AwaitingFix.ToList();
        var hotspots = await issues
            .GroupBy(i => new { i.ModuleId, i.Module.Name, i.Project.ProjectKey })
            .Select(g => new { g.Key.ModuleId, g.Key.Name, g.Key.ProjectKey, Count = g.Count(), Open = g.Count(i => open.Contains(i.Status)) })
            .OrderByDescending(m => m.Count).ThenBy(m => m.Name)
            .Take(TopModules)
            .Select(m => new ModuleHotspot(m.ModuleId, m.Name, m.ProjectKey, m.Count, m.Open))
            .ToListAsync(cancellationToken);

        var total = await issues.CountAsync(cancellationToken);
        var rejectedByType = await issues.Where(i => i.Status == IssueStatus.Rejected)
            .GroupBy(i => i.RejectType)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var rejected = rejectedByType.Sum(r => r.Count);
        var rejectedDto = new RejectedDto(
            rejected,
            total,
            total == 0 ? 0 : Analytics.Round((double)rejected / total, 3),
            Enum.GetValues<RejectType>().Select(t => new RejectTypeCount(t, rejectedByType.Where(r => r.Key == t).Sum(r => r.Count))).ToList());

        var weeks = range.Weeks();
        var created = await issues
            .Where(i => i.CreatedAt >= range.Start && i.CreatedAt < range.End)
            .Select(i => new { i.CreatedAt, i.Severity })
            .ToListAsync(cancellationToken);
        var trend = new WeeklySeries<Severity>(
            weeks,
            Analytics.SeverityOrder.Select(severity =>
            {
                var perWeek = created.Where(c => c.Severity == severity).CountBy(c => AnalyticsRange.WeekOf(WorkingCalendar.IstDate(c.CreatedAt))).ToDictionary();
                return new WeeklySeriesLine<Severity>(severity, severity.ToString(), weeks.Select(w => perWeek.GetValueOrDefault(w)).ToList());
            }).ToList());

        return new QualityDto(
            range.From,
            range.To,
            leakage,
            turnaround,
            Enum.GetValues<AgeingBucket>().Select(b => new AgeingCount(b, Label(b), ageing.GetValueOrDefault(b))).ToList(),
            hotspots,
            rejectedDto,
            trend);
    }

    /// <summary>The retest queue's ageing buckets (ADR-027): &lt; 1 day, 1–2, 3–5, &gt; 5 days waiting.</summary>
    public static AgeingBucket Bucket(double daysWaiting) => Math.Floor(daysWaiting) switch
    {
        < 1 => AgeingBucket.UnderOneDay,
        <= 2 => AgeingBucket.OneToTwoDays,
        <= 5 => AgeingBucket.ThreeToFiveDays,
        _ => AgeingBucket.OverFiveDays,
    };

    private static string Label(AgeingBucket bucket) => bucket switch
    {
        AgeingBucket.UnderOneDay => "< 1 day",
        AgeingBucket.OneToTwoDays => "1–2 days",
        AgeingBucket.ThreeToFiveDays => "3–5 days",
        _ => "> 5 days",
    };
}
