using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Analytics;

/// <param name="TestingDays">Working days from the start to testing completed (or to now while testing).</param>
public sealed record SprintHeaderDto(
    int Id,
    string Name,
    int ProjectId,
    string ProjectKey,
    string ProjectName,
    string ClientName,
    string PhaseName,
    SprintStatus Status,
    DateTimeOffset StartedAt,
    double TestingDays,
    DateTimeOffset? TestingCompletedAt,
    DateTimeOffset? ClosedAt,
    int IssueCount);

/// <param name="Open">Open issues at the end of the day; null for days still to come.</param>
/// <param name="Ideal">Straight line from the open count at testing completed to zero on the latest due date.</param>
public sealed record BurndownPoint(DateOnly Date, int? Open, double? Ideal);

public sealed record BurndownDto(DateOnly? LatestDueDate, IReadOnlyList<BurndownPoint> Points);

public sealed record HeatmapCell(int ModuleId, Severity Severity, int Count);

public sealed record ModuleLookup(int Id, string Name);

public sealed record ModuleSeverityHeatmap(IReadOnlyList<ModuleLookup> Modules, IReadOnlyList<Severity> Severities, IReadOnlyList<HeatmapCell> Cells);

public sealed record TypeCount(IssueType Type, int Count);

public sealed record ReopenedIssueDto(string Key, string Title, int ReopenCount, IssueStatus Status, string? AssigneeName);

public sealed record ReopenedDto(int Count, IReadOnlyList<ReopenedIssueDto> Items);

public sealed record SprintComparisonDto(int SprintId, string Name, string PhaseName, bool Current, int Total, IReadOnlyList<SeverityCount> BySeverity);

/// <summary>Sprint dashboard (plan 9.2).</summary>
public sealed record SprintDashboardDto(
    SprintHeaderDto Sprint,
    IReadOnlyList<StatusGroupCount> Progress,
    BurndownDto Burndown,
    IReadOnlyList<DailyCount> FoundPerDay,
    ModuleSeverityHeatmap ModuleSeverity,
    IReadOnlyList<TypeCount> ByType,
    ReopenedDto Reopened,
    IReadOnlyList<SprintComparisonDto> Comparison);

public sealed class SprintDashboardHandler(IApplicationDbContext db, IClock clock)
{
    public const int ReopenedItems = 20;

    public async Task<Result<SprintDashboardDto>> HandleAsync(int sprintId, CancellationToken cancellationToken)
    {
        var sprint = await db.TestingSprints.AsNoTracking()
            .Where(s => s.Id == sprintId)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.ProjectId,
                s.Project.ProjectKey,
                ProjectName = s.Project.Name,
                ClientName = s.Project.Client.Name,
                PhaseName = s.Phase.Name,
                s.Status,
                s.StartedAt,
                s.TestingCompletedAt,
                s.ClosedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (sprint is null)
        {
            return Error.NotFound("Sprint");
        }

        var now = clock.UtcNow;
        var holidays = await Analytics.HolidaysAsync(db, cancellationToken);
        var issues = db.Issues.AsNoTracking().Where(i => i.SprintId == sprintId);
        var rows = await issues
            .Select(i => new { i.Id, i.CreatedAt, i.DueDate, i.ModuleId, i.Severity, i.Type })
            .ToListAsync(cancellationToken);

        var header = new SprintHeaderDto(
            sprint.Id, sprint.Name, sprint.ProjectId, sprint.ProjectKey, sprint.ProjectName, sprint.ClientName, sprint.PhaseName, sprint.Status,
            sprint.StartedAt,
            Analytics.Round(WorkingCalendar.WorkingDaysBetween(sprint.StartedAt, sprint.TestingCompletedAt ?? now, holidays)),
            sprint.TestingCompletedAt, sprint.ClosedAt, rows.Count);

        var foundUntil = sprint.TestingCompletedAt ?? now;
        var found = rows.Where(r => r.CreatedAt <= foundUntil).CountBy(r => WorkingCalendar.IstDate(r.CreatedAt)).ToDictionary();
        var foundPerDay = found.Count == 0
            ? []
            : Days(WorkingCalendar.IstDate(sprint.StartedAt), WorkingCalendar.IstDate(foundUntil))
                .Select(d => new DailyCount(d, found.GetValueOrDefault(d)))
                .ToList();

        var modules = await db.Modules.AsNoTracking()
            .Where(m => rows.Select(r => r.ModuleId).Distinct().Contains(m.Id))
            .OrderBy(m => m.Name)
            .Select(m => new ModuleLookup(m.Id, m.Name))
            .ToListAsync(cancellationToken);
        var cells = rows.GroupBy(r => (r.ModuleId, r.Severity)).Select(g => new HeatmapCell(g.Key.ModuleId, g.Key.Severity, g.Count())).ToList();

        var reopened = await issues.Where(i => i.ReopenCount > 0)
            .OrderByDescending(i => i.ReopenCount).ThenBy(i => i.Number)
            .Select(i => new ReopenedIssueDto(i.IssueKey, i.Title, i.ReopenCount, i.Status, i.Assignee != null ? i.Assignee.FullName : null))
            .ToListAsync(cancellationToken);

        return new SprintDashboardDto(
            header,
            await Analytics.StatusGroupsAsync(issues, cancellationToken),
            await BurndownAsync(sprint.TestingCompletedAt, sprint.ClosedAt, rows.Select(r => (r.Id, r.CreatedAt, r.DueDate)).ToList(), now, cancellationToken),
            foundPerDay,
            new ModuleSeverityHeatmap(modules, Analytics.SeverityOrder, cells),
            rows.CountBy(r => r.Type).OrderByDescending(x => x.Value).Select(x => new TypeCount(x.Key, x.Value)).ToList(),
            new ReopenedDto(reopened.Count, reopened.Take(ReopenedItems).ToList()),
            await ComparisonAsync(sprint.ProjectId, sprintId, cancellationToken));
    }

    /// <summary>
    /// Open issues at the end of each IST day since testing completed (from the status history), and the ideal line
    /// down to zero on the latest due date. Empty while the sprint is still in testing.
    /// </summary>
    private async Task<BurndownDto> BurndownAsync(
        DateTimeOffset? testingCompletedAt,
        DateTimeOffset? closedAt,
        IReadOnlyList<(int Id, DateTimeOffset CreatedAt, DateTimeOffset? DueDate)> issues,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latestDue = issues.Max(i => i.DueDate) is { } due ? WorkingCalendar.IstDate(due) : (DateOnly?)null;
        if (testingCompletedAt is not { } completed || issues.Count == 0)
        {
            return new BurndownDto(latestDue, []);
        }

        var ids = issues.Select(i => i.Id).ToList();
        var changes = (await db.IssueActivities.AsNoTracking()
                .Where(a => ids.Contains(a.IssueId) && a.Kind == ActivityKind.StatusChanged)
                .Select(a => new { a.IssueId, a.Id, a.CreatedAt, a.NewValue })
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.IssueId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => (a.CreatedAt, a.NewValue)).ToList());

        var open = Analytics.OpenStatusNames.ToHashSet();
        int OpenAt(DateTimeOffset moment) => issues.Count(issue =>
        {
            if (issue.CreatedAt > moment)
            {
                return false;
            }

            var status = changes.TryGetValue(issue.Id, out var history)
                ? history.LastOrDefault(c => c.CreatedAt <= moment).NewValue
                : null;
            return status is null || open.Contains(status);
        });

        var first = WorkingCalendar.IstDate(completed);
        var today = WorkingCalendar.IstDate(closedAt ?? now);
        var last = latestDue is { } dueDay && dueDay > today ? dueDay : today;
        var startOpen = OpenAt(completed);
        var idealDays = latestDue is { } end && end > first ? end.DayNumber - first.DayNumber : 0;

        var points = Days(first, last).Select(day =>
        {
            int? openCount = day <= today ? OpenAt(day == today && closedAt is null ? now : AnalyticsRange.StartOf(day.AddDays(1))) : null;
            double? ideal = idealDays > 0 && day <= latestDue
                ? Analytics.Round(startOpen * (1 - (double)(day.DayNumber - first.DayNumber) / idealDays))
                : null;
            return new BurndownPoint(day, openCount, ideal);
        }).ToList();
        return new BurndownDto(latestDue, points);
    }

    /// <summary>Issues per sprint of the project by severity, in start order (e.g. Early 45 → UAT 74 → LIVE 8).</summary>
    private async Task<IReadOnlyList<SprintComparisonDto>> ComparisonAsync(int projectId, int currentSprintId, CancellationToken cancellationToken)
    {
        var sprints = await db.TestingSprints.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.StartedAt)
            .Select(s => new { s.Id, s.Name, PhaseName = s.Phase.Name })
            .ToListAsync(cancellationToken);
        var counts = await db.Issues.AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .GroupBy(i => new { i.SprintId, i.Severity })
            .Select(g => new { g.Key.SprintId, g.Key.Severity, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return sprints.Select(s =>
        {
            var bySeverity = Analytics.SeverityOrder
                .Select(sev => new SeverityCount(sev, counts.Where(c => c.SprintId == s.Id && c.Severity == sev).Sum(c => c.Count)))
                .ToList();
            return new SprintComparisonDto(s.Id, s.Name, s.PhaseName, s.Id == currentSprintId, bySeverity.Sum(c => c.Count), bySeverity);
        }).ToList();
    }

    private static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
