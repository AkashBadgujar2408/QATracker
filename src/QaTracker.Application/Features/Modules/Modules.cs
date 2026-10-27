using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;

namespace QaTracker.Application.Features.Modules;

public sealed record ModuleDto(int Id, int ProjectId, string Name, bool IsActive, string RowVersion)
{
    public static readonly Expression<Func<Module, ModuleDto>> Projection =
        m => new ModuleDto(m.Id, m.ProjectId, m.Name, m.IsActive, Convert.ToBase64String(m.RowVersion));
}

public sealed record ListModulesQuery(int ProjectId, bool IncludeInactive);

public sealed class ListModulesHandler(IApplicationDbContext db)
{
    public async Task<Result<IReadOnlyList<ModuleDto>>> HandleAsync(ListModulesQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == query.ProjectId, cancellationToken))
        {
            return Error.NotFound("Project");
        }

        return await db.Modules.AsNoTracking()
            .Where(m => m.ProjectId == query.ProjectId && (query.IncludeInactive || m.IsActive))
            .OrderBy(m => m.Name)
            .Select(ModuleDto.Projection)
            .ToListAsync(cancellationToken);
    }
}

public sealed record CreateModuleCommand(int ProjectId, string Name);

public sealed class CreateModuleValidator : AbstractValidator<CreateModuleCommand>
{
    public CreateModuleValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
}

/// <summary>Add a module (functional area) to a project – also the quick-add from the Testing screen.</summary>
public sealed class CreateModuleHandler(
    IValidator<CreateModuleCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ModuleDto>> HandleAsync(CreateModuleCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound("Project");
        }

        if (!project.IsActive)
        {
            return Error.Rule("project_inactive", $"Project '{project.Name}' is inactive.");
        }

        var name = command.Name.Trim();
        if (await db.Modules.AnyAsync(m => m.ProjectId == command.ProjectId && m.Name == name, cancellationToken))
        {
            return Error.Conflict("module_name_taken", $"Project '{project.Name}' already has a module named '{name}'.");
        }

        var module = new Module
        {
            ProjectId = command.ProjectId,
            Name = name,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedById = currentUser.UserId,
        };
        db.Modules.Add(module);

        if (await db.TrySaveAsync("A module with this name", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.Modules.AsNoTracking().Where(m => m.Id == module.Id).Select(ModuleDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record UpdateModuleCommand(int Id, string Name, string RowVersion);

public sealed class UpdateModuleValidator : AbstractValidator<UpdateModuleCommand>
{
    public UpdateModuleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RowVersion).ValidRowVersion();
    }
}

public sealed class UpdateModuleHandler(
    IValidator<UpdateModuleCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ModuleDto>> HandleAsync(UpdateModuleCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var module = await db.Modules.SingleOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (module is null)
        {
            return Error.NotFound("Module");
        }

        var name = command.Name.Trim();
        if (await db.Modules.AnyAsync(m => m.Id != module.Id && m.ProjectId == module.ProjectId && m.Name == name, cancellationToken))
        {
            return Error.Conflict("module_name_taken", $"This project already has a module named '{name}'.");
        }

        db.ExpectRowVersion(module, command.RowVersion);
        module.Name = name;
        module.UpdatedAt = clock.UtcNow;
        module.UpdatedById = currentUser.UserId;

        if (await db.TrySaveAsync("This module", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.Modules.AsNoTracking().Where(m => m.Id == module.Id).Select(ModuleDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record SetModuleActiveCommand(int Id, bool IsActive);

public sealed class SetModuleActiveHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<Result<ModuleDto>> HandleAsync(SetModuleActiveCommand command, CancellationToken cancellationToken)
    {
        var module = await db.Modules.SingleOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (module is null)
        {
            return Error.NotFound("Module");
        }

        module.IsActive = command.IsActive;
        module.UpdatedAt = clock.UtcNow;
        module.UpdatedById = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);

        return await db.Modules.AsNoTracking().Where(m => m.Id == module.Id).Select(ModuleDto.Projection).SingleAsync(cancellationToken);
    }
}
