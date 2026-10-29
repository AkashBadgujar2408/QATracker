using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Analytics;

public enum Health
{
    Green,
    Amber,
    Red,
}

public sealed record LastSprintDto(int SprintId, string Name, string ProjectKey, SprintStatus Status, DateTimeOffset StartedAt);

public sealed record ClientCardDto(int ClientId, string Name, int Open, int Critical, int Overdue, LastSprintDto? LastSprint, Health Health);

public sealed record ClientSeverityDto(int ClientId, string Name, IReadOnlyList<SeverityCount> BySeverity);

/// <summary>Issues of a project by the type of environment they were found on (UAT vs LIVE).</summary>
public sealed record EnvironmentSplitDto(int ProjectId, string ProjectKey, string ProjectName, int Uat, int Live, int Other, int NotSet);

public sealed record ClientMetric(int ClientId, string Name, double Value, int Count);

/// <summary>Client health dashboard (plan 9.5).</summary>
public sealed record ClientsDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ClientCardDto> Clients,
    IReadOnlyList<ClientSeverityDto> OpenBySeverity,
    IReadOnlyList<EnvironmentSplitDto> UatVsLive,
    IReadOnlyList<ClientMetric> AverageClosureDays);

public sealed class ClientsHandler(IValidator<AnalyticsFilter> validator, IApplicationDbContext db, IClock clock)
{
    /// <summary>Red: a critical issue is open or more than 3 are overdue. Amber: anything overdue. Otherwise green.</summary>
    public static Health HealthOf(int critical, int overdue) =>
        critical > 0 || overdue > 3 ? Health.Red : overdue > 0 ? Health.Amber : Health.Green;

    public async Task<Result<ClientsDto>> HandleAsync(AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(filter, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var range = AnalyticsRange.Resolve(filter, now);
        var holidays = await Analytics.HolidaysAsync(db, cancellationToken);
        var issues = Analytics.Scope(db, filter);

        var clientsQuery = db.Clients.AsNoTracking().Where(c => c.IsActive);
        if (filter.ClientId is { } clientId)
        {
            clientsQuery = clientsQuery.Where(c => c.Id == clientId);
        }

        var clients = await clientsQuery.OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);

        var open = await issues.Open()
            .GroupBy(i => new { i.Project.ClientId, i.Severity })
            .Select(g => new { g.Key.ClientId, g.Key.Severity, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var overdue = await issues.Overdue(now)
            .GroupBy(i => i.Project.ClientId)
            .Select(g => new { ClientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClientId, x => x.Count, cancellationToken);

        var sprints = db.TestingSprints.AsNoTracking();
        if (filter.ProjectId is { } projectId)
        {
            sprints = sprints.Where(s => s.ProjectId == projectId);
        }

        var lastSprints = await sprints
            .GroupBy(s => s.Project.ClientId)
            .Select(g => g.OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id)
                .Select(s => new { s.Project.ClientId, s.Id, s.Name, s.Project.ProjectKey, s.Status, s.StartedAt })
                .First())
            .ToDictionaryAsync(s => s.ClientId, cancellationToken);

        var cards = clients.Select(c =>
        {
            var clientOpen = open.Where(o => o.ClientId == c.Id).ToList();
            var critical = clientOpen.Where(o => o.Severity == Domain.Enums.Severity.Critical).Sum(o => o.Count);
            var late = overdue.GetValueOrDefault(c.Id);
            var last = lastSprints.GetValueOrDefault(c.Id);
            return new ClientCardDto(
                c.Id,
                c.Name,
                clientOpen.Sum(o => o.Count),
                critical,
                late,
                last is null ? null : new LastSprintDto(last.Id, last.Name, last.ProjectKey, last.Status, last.StartedAt),
                HealthOf(critical, late));
        })
            .OrderByDescending(c => c.Health).ThenByDescending(c => c.Open).ThenBy(c => c.Name)
            .ToList();

        var openBySeverity = clients
            .Select(c => new ClientSeverityDto(c.Id, c.Name,
                Analytics.SeverityOrder.Select(s => new SeverityCount(s, open.Where(o => o.ClientId == c.Id && o.Severity == s).Sum(o => o.Count))).ToList()))
            .Where(c => c.BySeverity.Any(s => s.Count > 0))
            .OrderByDescending(c => c.BySeverity.Sum(s => s.Count))
            .ToList();

        var splitRows = await issues
            .GroupBy(i => new
            {
                i.ProjectId,
                i.Project.ProjectKey,
                i.Project.Name,
                Type = i.FoundOnEnvironment != null ? (EnvironmentType?)i.FoundOnEnvironment.Type : null,
            })
            .Select(g => new { g.Key.ProjectId, g.Key.ProjectKey, g.Key.Name, g.Key.Type, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var split = splitRows.GroupBy(r => new { r.ProjectId, r.ProjectKey, r.Name })
            .Select(g =>
            {
                int Count(EnvironmentType? type) => g.Where(r => r.Type == type).Sum(r => r.Count);
                return new EnvironmentSplitDto(g.Key.ProjectId, g.Key.ProjectKey, g.Key.Name,
                    Count(EnvironmentType.UAT), Count(EnvironmentType.LIVE), Count(EnvironmentType.Other), Count(null));
            })
            .OrderBy(p => p.ProjectKey)
            .ToList();

        var closures = await issues
            .Where(i => i.ClosedAt >= range.Start && i.ClosedAt < range.End)
            .Select(i => new { i.Project.ClientId, i.CreatedAt, ClosedAt = i.ClosedAt!.Value })
            .ToListAsync(cancellationToken);
        var names = clients.ToDictionary(c => c.Id, c => c.Name);
        var closure = closures.Where(c => names.ContainsKey(c.ClientId)).GroupBy(c => c.ClientId)
            .Select(g => new ClientMetric(g.Key, names[g.Key],
                Analytics.Round(g.Average(c => WorkingCalendar.WorkingDaysBetween(c.CreatedAt, c.ClosedAt, holidays))), g.Count()))
            .OrderByDescending(m => m.Value)
            .ToList();

        return new ClientsDto(range.From, range.To, cards, openBySeverity, split, closure);
    }
}
