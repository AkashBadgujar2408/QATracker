using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;

namespace QaTracker.Application.Features.Projects;

public sealed record CreateProjectCommand(int ClientId, string Name, string Key, bool RequireLiveVerification = false)
    : IProjectFields;

public sealed class CreateProjectValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectValidator() => ProjectRules.Apply(this);
}

/// <summary>Create a project under an active client (also the quick-add – name + key).</summary>
public sealed class CreateProjectHandler(
    IValidator<CreateProjectCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ProjectDto>> HandleAsync(CreateProjectCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == command.ClientId, cancellationToken);
        if (client is null)
        {
            return Error.NotFound("Client");
        }

        if (!client.IsActive)
        {
            return Error.Rule("client_inactive", $"Client '{client.Name}' is inactive. Activate it before adding projects.");
        }

        var name = command.Name.Trim();
        var key = ProjectKeys.Normalize(command.Key);
        if (await ProjectChecks.FindConflictAsync(db, command.ClientId, name, key, exceptId: null, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var project = new Project
        {
            ClientId = command.ClientId,
            Name = name,
            ProjectKey = key,
            NextIssueNumber = 1,
            RequireLiveVerification = command.RequireLiveVerification,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedById = currentUser.UserId,
        };
        db.Projects.Add(project);

        if (await db.TrySaveAsync("A project with this name or key", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.Projects.AsNoTracking().Where(p => p.Id == project.Id).Select(ProjectDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record UpdateProjectCommand(int Id, string Name, string Key, bool RequireLiveVerification, string RowVersion)
    : IProjectFields;

public sealed class UpdateProjectValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectValidator()
    {
        ProjectRules.Apply(this);
        RuleFor(x => x.RowVersion).ValidRowVersion();
    }
}

/// <summary>Edit a project. The key can only change while the project has no issues (issue keys are permanent).</summary>
public sealed class UpdateProjectHandler(
    IValidator<UpdateProjectCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ProjectDto>> HandleAsync(UpdateProjectCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (project is null)
        {
            return Error.NotFound("Project");
        }

        var name = command.Name.Trim();
        var key = ProjectKeys.Normalize(command.Key);
        if (key != project.ProjectKey && await db.Issues.AnyAsync(i => i.ProjectId == project.Id, cancellationToken))
        {
            return Error.Rule("key_locked", $"The key of a project with issues cannot change (issues are already named {project.ProjectKey}-…).");
        }

        if (await ProjectChecks.FindConflictAsync(db, project.ClientId, name, key, project.Id, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        db.ExpectRowVersion(project, command.RowVersion);
        project.Name = name;
        project.ProjectKey = key;
        project.RequireLiveVerification = command.RequireLiveVerification;
        project.UpdatedAt = clock.UtcNow;
        project.UpdatedById = currentUser.UserId;

        if (await db.TrySaveAsync("This project", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.Projects.AsNoTracking().Where(p => p.Id == project.Id).Select(ProjectDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record SetProjectActiveCommand(int Id, bool IsActive);

public sealed class SetProjectActiveHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<Result<ProjectDto>> HandleAsync(SetProjectActiveCommand command, CancellationToken cancellationToken)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (project is null)
        {
            return Error.NotFound("Project");
        }

        project.IsActive = command.IsActive;
        project.UpdatedAt = clock.UtcNow;
        project.UpdatedById = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);

        return await db.Projects.AsNoTracking().Where(p => p.Id == project.Id).Select(ProjectDto.Projection).SingleAsync(cancellationToken);
    }
}

internal static class ProjectChecks
{
    /// <summary>Project keys are unique globally; project names are unique within their client.</summary>
    public static async Task<Error?> FindConflictAsync(
        IApplicationDbContext db, int clientId, string name, string key, int? exceptId, CancellationToken cancellationToken)
    {
        var others = db.Projects.Where(p => p.Id != exceptId);

        if (await others.AnyAsync(p => p.ProjectKey == key, cancellationToken))
        {
            return Error.Conflict("project_key_taken", $"Project key '{key}' is already used by another project.");
        }

        if (await others.AnyAsync(p => p.ClientId == clientId && p.Name == name, cancellationToken))
        {
            return Error.Conflict("project_name_taken", $"This client already has a project named '{name}'.");
        }

        return null;
    }
}
