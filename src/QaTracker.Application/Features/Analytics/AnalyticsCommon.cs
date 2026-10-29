using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Analytics;

/// <summary>
/// The global dashboard filter (plan 9): client, project, sprint and an IST date range (default the last 30 days).
/// Scope filters apply to every widget; the date range applies to widgets about events in time (created / closed /
/// fixed / retested per day or week, fix and closure times, recent activity). Current-state widgets (open, overdue,
/// workload, health) always show today.
/// </summary>
public sealed record AnalyticsFilter(int? ClientId = null, int? ProjectId = null, int? SprintId = null, DateOnly? From = null, DateOnly? To = null);

public sealed class AnalyticsFilterValidator : AbstractValidator<AnalyticsFilter>
{
    public const int MaxDays = 366;

    public AnalyticsFilterValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("The end of the date range must not be before its start.");
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber < MaxDays)
            .WithName("from")
            .WithMessage($"Choose a date range of at most {MaxDays} days.");
    }
}

/// <summary>The resolved range: IST days and the UTC instants that bound them.</summary>
public sealed record AnalyticsRange(DateOnly From, DateOnly To, DateTimeOffset Start, DateTimeOffset End)
{
    public const int DefaultDays = 30;

    public static AnalyticsRange Resolve(AnalyticsFilter filter, DateTimeOffset now)
    {
        var to = filter.To ?? WorkingCalendar.IstDate(now);
        var from = filter.From ?? to.AddDays(-(DefaultDays - 1));
        return new AnalyticsRange(from, to, StartOf(from), StartOf(to.AddDays(1)));
    }

    public static DateTimeOffset StartOf(DateOnly istDate) =>
        new DateTimeOffset(istDate.ToDateTime(TimeOnly.MinValue), WorkingCalendar.Ist).ToUniversalTime();

    public IEnumerable<DateOnly> Days()
    {
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    /// <summary>Mondays of the IST weeks the range touches.</summary>
    public IReadOnlyList<DateOnly> Weeks()
    {
        var weeks = new List<DateOnly>();
        for (var week = WeekOf(From); week <= To; week = week.AddDays(7))
        {
            weeks.Add(week);
        }

        return weeks;
    }

    public static DateOnly WeekOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}

/// <summary>Status groups of the dashboards (plan 9.1 donut, 9.2 progress).</summary>
public enum StatusGroup
{
    Open,
    Fixed,
    Verified,
    Closed,
    Rejected,
}

public sealed record StatusGroupCount(StatusGroup Group, IReadOnlyList<IssueStatus> Statuses, int Count);

public sealed record SeverityCount(Severity Severity, int Count);

public sealed record DailyCount(DateOnly Date, int Count);

/// <summary>A value per developer (hours, a rate 0–1, …) and how many issues it is based on.</summary>
public sealed record UserMetric(int UserId, string Name, string? AvatarColor, double Value, int Count);

/// <summary>Weekly series: one value per week (Mondays, IST) for each named series.</summary>
public sealed record WeeklySeries<TKey>(IReadOnlyList<DateOnly> Weeks, IReadOnlyList<WeeklySeriesLine<TKey>> Series);

public sealed record WeeklySeriesLine<TKey>(TKey Key, string Name, IReadOnlyList<int> Values);

internal static class Analytics
{
    public static readonly IReadOnlyList<Severity> SeverityOrder =
        [Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.NotAssessed];

    public static readonly IReadOnlyDictionary<StatusGroup, IReadOnlyList<IssueStatus>> Groups = new Dictionary<StatusGroup, IReadOnlyList<IssueStatus>>
    {
        [StatusGroup.Open] = IssueStates.AwaitingFix,
        [StatusGroup.Fixed] = [IssueStatus.Fixed],
        [StatusGroup.Verified] = [IssueStatus.Verified],
        [StatusGroup.Closed] = [IssueStatus.Closed],
        [StatusGroup.Rejected] = [IssueStatus.Rejected],
    };

    /// <summary>Names of the open statuses, as stored in StatusChanged activities.</summary>
    public static readonly IReadOnlyList<string> OpenStatusNames = IssueStates.AwaitingFix.Select(s => s.ToString()).ToList();

    public static IQueryable<Issue> Scope(IApplicationDbContext db, AnalyticsFilter filter)
    {
        var issues = db.Issues.AsNoTracking();
        if (filter.ClientId is { } clientId)
        {
            issues = issues.Where(i => i.Project.ClientId == clientId);
        }

        if (filter.ProjectId is { } projectId)
        {
            issues = issues.Where(i => i.ProjectId == projectId);
        }

        if (filter.SprintId is { } sprintId)
        {
            issues = issues.Where(i => i.SprintId == sprintId);
        }

        return issues;
    }

    public static IQueryable<Issue> Open(this IQueryable<Issue> issues)
    {
        var open = IssueStates.AwaitingFix.ToList();
        return issues.Where(i => open.Contains(i.Status));
    }

    public static IQueryable<Issue> Overdue(this IQueryable<Issue> issues, DateTimeOffset now) =>
        issues.Open().Where(i => i.DueDate != null && i.DueDate < now);

    public static async Task<IReadOnlyList<StatusGroupCount>> StatusGroupsAsync(IQueryable<Issue> issues, CancellationToken cancellationToken)
    {
        var counts = await issues.GroupBy(i => i.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return Groups.Select(g => new StatusGroupCount(g.Key, g.Value, g.Value.Sum(s => counts.GetValueOrDefault(s)))).ToList();
    }

    public static async Task<IReadOnlyList<SeverityCount>> BySeverityAsync(IQueryable<Issue> issues, CancellationToken cancellationToken)
    {
        var counts = await issues.GroupBy(i => i.Severity).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return SeverityOrder.Select(s => new SeverityCount(s, counts.GetValueOrDefault(s))).ToList();
    }

    public static async Task<IReadOnlySet<DateOnly>> HolidaysAsync(IApplicationDbContext db, CancellationToken cancellationToken) =>
        (await db.Holidays.AsNoTracking().Select(h => h.Date).ToListAsync(cancellationToken)).ToHashSet();

    public static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    public static double Round(double value, int digits = 1) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
