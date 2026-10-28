using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Sprints;

/// <param name="DefaultEnvironmentId">Pre-fills "Found on environment" of new issues; optional.</param>
public sealed record StartSprintCommand(int ProjectId, string Name, int PhaseId, int? DefaultEnvironmentId, string? Note = null);

public sealed class StartSprintValidator : AbstractValidator<StartSprintCommand>
{
    public StartSprintValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PhaseId).GreaterThan(0);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

/// <summary>Start a testing sprint inline from the Testing screen (plan 6.1): start date = now, one Testing sprint per project.</summary>
public sealed class StartSprintHandler(
    IValidator<StartSprintCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<SprintDto>> HandleAsync(StartSprintCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == command.ProjectId)
            .Select(p => new { p.IsActive, ClientIsActive = p.Client.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return Error.NotFound("Project");
        }

        var openSprint = await db.TestingSprints.AsNoTracking()
            .Where(s => s.ProjectId == command.ProjectId && s.Status == SprintStatus.Testing)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken);
        var check = SprintRules.CanStart(currentUser.Role, project.IsActive && project.ClientIsActive, openSprint);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        if (!await db.SprintPhases.AnyAsync(p => p.Id == command.PhaseId && p.IsActive, cancellationToken))
        {
            return Error.Validation(nameof(command.PhaseId), "Choose an active phase.");
        }

        if (command.DefaultEnvironmentId is { } environmentId
            && !await db.ProjectEnvironments.AnyAsync(e => e.Id == environmentId && e.ProjectId == command.ProjectId && e.IsActive, cancellationToken))
        {
            return Error.Validation(nameof(command.DefaultEnvironmentId), "Choose an active environment of this project.");
        }

        var name = command.Name.Trim();
        if (await db.TestingSprints.AnyAsync(s => s.ProjectId == command.ProjectId && s.Name == name, cancellationToken))
        {
            return Error.Conflict("sprint_name_taken", $"This project already has a sprint named '{name}'.");
        }

        var now = clock.UtcNow;
        var sprint = new TestingSprint
        {
            ProjectId = command.ProjectId,
            Name = name,
            PhaseId = command.PhaseId,
            DefaultEnvironmentId = command.DefaultEnvironmentId,
            Status = SprintStatus.Testing,
            StartedAt = now,
            StartedById = currentUser.UserId,
        };
        sprint.SprintEvents.Add(new SprintEvent
        {
            UserId = currentUser.UserId,
            Kind = SprintEventKind.Started,
            Note = SprintText.Clean(command.Note),
            At = now,
        });
        db.TestingSprints.Add(sprint);

        // The filtered unique index (one Testing sprint per project) catches a parallel start → 409.
        if (await db.TrySaveAsync("A sprint in testing for this project", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.TestingSprints.AsNoTracking().Where(s => s.Id == sprint.Id).Select(SprintDto.Projection).SingleAsync(cancellationToken);
    }
}

internal static class SprintText
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
