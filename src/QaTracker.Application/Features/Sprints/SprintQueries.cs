using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Sprints;

/// <summary>Sprints of a project, newest first.</summary>
public sealed class ListSprintsHandler(IApplicationDbContext db)
{
    public async Task<Result<IReadOnlyList<SprintDto>>> HandleAsync(int projectId, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return Error.NotFound("Project");
        }

        return await db.TestingSprints.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id)
            .Select(SprintDto.Projection)
            .ToListAsync(cancellationToken);
    }
}

public sealed class GetSprintHandler(IApplicationDbContext db)
{
    public async Task<Result<SprintDetailDto>> HandleAsync(int id, CancellationToken cancellationToken)
    {
        var sprint = await db.TestingSprints.AsNoTracking().Where(s => s.Id == id).Select(SprintDto.Projection).SingleOrDefaultAsync(cancellationToken);
        if (sprint is null)
        {
            return Error.NotFound("Sprint");
        }

        var events = await db.SprintEvents.AsNoTracking()
            .Where(e => e.SprintId == id)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new SprintEventDto(e.Kind, e.User.FullName, e.Note, e.At))
            .ToListAsync(cancellationToken);

        return new SprintDetailDto(sprint, events);
    }
}

public sealed record SprintNameSuggestion(string Name);

/// <summary>"Sprint 2 – UAT" for the start dialog (plan 6.1).</summary>
public sealed class SuggestSprintNameHandler(IApplicationDbContext db)
{
    public async Task<Result<SprintNameSuggestion>> HandleAsync(int projectId, int phaseId, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return Error.NotFound("Project");
        }

        var phase = await db.SprintPhases.AsNoTracking().Where(p => p.Id == phaseId).Select(p => p.Name).SingleOrDefaultAsync(cancellationToken);
        if (phase is null)
        {
            return Error.NotFound("Phase");
        }

        var names = await db.TestingSprints.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id)
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

        return new SprintNameSuggestion(SprintNames.Suggest(names, phase));
    }
}

public sealed record CountBySeverity(Severity Severity, int Count);

public sealed record CountByModule(int ModuleId, string ModuleName, int Count);

/// <summary>What "Mark Testing Completed" will hand over (plan 6.1 confirmation summary).</summary>
public sealed record CompletionSummaryDto(
    int SprintId,
    SprintStatus Status,
    int IssueCount,
    int UnassignedCount,
    int NotAssessedCount,
    IReadOnlyList<CountBySeverity> BySeverity,
    IReadOnlyList<CountByModule> ByModule);

public sealed class CompletionSummaryHandler(IApplicationDbContext db)
{
    public async Task<Result<CompletionSummaryDto>> HandleAsync(int sprintId, CancellationToken cancellationToken)
    {
        var status = await db.TestingSprints.AsNoTracking().Where(s => s.Id == sprintId).Select(s => (SprintStatus?)s.Status).SingleOrDefaultAsync(cancellationToken);
        if (status is null)
        {
            return Error.NotFound("Sprint");
        }

        var issues = db.Issues.AsNoTracking().Where(i => i.SprintId == sprintId);
        var bySeverity = await issues.GroupBy(i => i.Severity)
            .Select(g => new CountBySeverity(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
        var byModule = await issues.GroupBy(i => new { i.ModuleId, i.Module.Name })
            .Select(g => new CountByModule(g.Key.ModuleId, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken);
        var unassigned = await issues.CountAsync(i => i.AssigneeId == null, cancellationToken);

        return new CompletionSummaryDto(
            sprintId,
            status.Value,
            bySeverity.Sum(s => s.Count),
            unassigned,
            bySeverity.Where(s => s.Severity == Severity.NotAssessed).Sum(s => s.Count),
            bySeverity.OrderBy(s => s.Severity).ToList(),
            byModule.OrderByDescending(m => m.Count).ThenBy(m => m.ModuleName).ToList());
    }
}
