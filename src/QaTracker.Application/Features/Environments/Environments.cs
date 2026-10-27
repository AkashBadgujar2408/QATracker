using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Environments;

/// <summary>A URL where the project runs (UAT 1 – https://uat.client.com).</summary>
public sealed record EnvironmentDto(int Id, int ProjectId, string Name, EnvironmentType Type, string Url, bool IsActive, string RowVersion)
{
    public static readonly Expression<Func<ProjectEnvironment, EnvironmentDto>> Projection =
        e => new EnvironmentDto(e.Id, e.ProjectId, e.Name, e.Type, e.Url, e.IsActive, Convert.ToBase64String(e.RowVersion));
}

public interface IEnvironmentFields
{
    string Name { get; }
    EnvironmentType Type { get; }
    string Url { get; }
}

internal static class EnvironmentRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IEnvironmentFields
    {
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        validator.RuleFor(x => x.Type).IsInEnum();
        validator.RuleFor(x => x.Url).NotEmpty().MaximumLength(500)
            .Must(url => Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("URL must be a full http(s) address, e.g. https://uat.client.com.");
    }
}

public sealed record ListEnvironmentsQuery(int ProjectId, bool IncludeInactive);

public sealed class ListEnvironmentsHandler(IApplicationDbContext db)
{
    public async Task<Result<IReadOnlyList<EnvironmentDto>>> HandleAsync(ListEnvironmentsQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == query.ProjectId, cancellationToken))
        {
            return Error.NotFound("Project");
        }

        return await db.ProjectEnvironments.AsNoTracking()
            .Where(e => e.ProjectId == query.ProjectId && (query.IncludeInactive || e.IsActive))
            .OrderBy(e => e.Type).ThenBy(e => e.Name)
            .Select(EnvironmentDto.Projection)
            .ToListAsync(cancellationToken);
    }
}

public sealed record CreateEnvironmentCommand(int ProjectId, string Name, EnvironmentType Type, string Url) : IEnvironmentFields;

public sealed class CreateEnvironmentValidator : AbstractValidator<CreateEnvironmentCommand>
{
    public CreateEnvironmentValidator() => EnvironmentRules.Apply(this);
}

public sealed class CreateEnvironmentHandler(
    IValidator<CreateEnvironmentCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<EnvironmentDto>> HandleAsync(CreateEnvironmentCommand command, CancellationToken cancellationToken)
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
        if (await db.ProjectEnvironments.AnyAsync(e => e.ProjectId == command.ProjectId && e.Name == name, cancellationToken))
        {
            return Error.Conflict("environment_name_taken", $"Project '{project.Name}' already has an environment named '{name}'.");
        }

        var environment = new ProjectEnvironment
        {
            ProjectId = command.ProjectId,
            Name = name,
            Type = command.Type,
            Url = command.Url.Trim(),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedById = currentUser.UserId,
        };
        db.ProjectEnvironments.Add(environment);

        if (await db.TrySaveAsync("An environment with this name", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.ProjectEnvironments.AsNoTracking().Where(e => e.Id == environment.Id).Select(EnvironmentDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record UpdateEnvironmentCommand(int Id, string Name, EnvironmentType Type, string Url, string RowVersion) : IEnvironmentFields;

public sealed class UpdateEnvironmentValidator : AbstractValidator<UpdateEnvironmentCommand>
{
    public UpdateEnvironmentValidator()
    {
        EnvironmentRules.Apply(this);
        RuleFor(x => x.RowVersion).ValidRowVersion();
    }
}

public sealed class UpdateEnvironmentHandler(
    IValidator<UpdateEnvironmentCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<EnvironmentDto>> HandleAsync(UpdateEnvironmentCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var environment = await db.ProjectEnvironments.SingleOrDefaultAsync(e => e.Id == command.Id, cancellationToken);
        if (environment is null)
        {
            return Error.NotFound("Environment");
        }

        var name = command.Name.Trim();
        if (await db.ProjectEnvironments.AnyAsync(e => e.Id != environment.Id && e.ProjectId == environment.ProjectId && e.Name == name, cancellationToken))
        {
            return Error.Conflict("environment_name_taken", $"This project already has an environment named '{name}'.");
        }

        db.ExpectRowVersion(environment, command.RowVersion);
        environment.Name = name;
        environment.Type = command.Type;
        environment.Url = command.Url.Trim();
        environment.UpdatedAt = clock.UtcNow;
        environment.UpdatedById = currentUser.UserId;

        if (await db.TrySaveAsync("This environment", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.ProjectEnvironments.AsNoTracking().Where(e => e.Id == environment.Id).Select(EnvironmentDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record SetEnvironmentActiveCommand(int Id, bool IsActive);

public sealed class SetEnvironmentActiveHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<Result<EnvironmentDto>> HandleAsync(SetEnvironmentActiveCommand command, CancellationToken cancellationToken)
    {
        var environment = await db.ProjectEnvironments.SingleOrDefaultAsync(e => e.Id == command.Id, cancellationToken);
        if (environment is null)
        {
            return Error.NotFound("Environment");
        }

        environment.IsActive = command.IsActive;
        environment.UpdatedAt = clock.UtcNow;
        environment.UpdatedById = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);

        return await db.ProjectEnvironments.AsNoTracking().Where(e => e.Id == environment.Id).Select(EnvironmentDto.Projection).SingleAsync(cancellationToken);
    }
}
