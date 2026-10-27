using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;

namespace QaTracker.Application.Features.Clients;

public sealed record CreateClientCommand(string Name, string? ContactPerson = null, string? Email = null, string? Phone = null)
    : IClientFields;

public sealed class CreateClientValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientValidator() => ClientRules.Apply(this);
}

/// <summary>Create a client (also the quick-add from pickers – only the name is required).</summary>
public sealed class CreateClientHandler(
    IValidator<CreateClientCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ClientDto>> HandleAsync(CreateClientCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var name = command.Name.Trim();
        if (await db.Clients.AnyAsync(c => c.Name == name, cancellationToken))
        {
            return Error.Conflict("client_name_taken", $"A client named '{name}' already exists.");
        }

        var client = new Client
        {
            Name = name,
            ContactPerson = ClientRules.Clean(command.ContactPerson),
            Email = ClientRules.Clean(command.Email),
            Phone = ClientRules.Clean(command.Phone),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedById = currentUser.UserId,
        };
        db.Clients.Add(client);

        if (await db.TrySaveAsync("A client with this name", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.Clients.AsNoTracking().Where(c => c.Id == client.Id).Select(ClientDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record UpdateClientCommand(int Id, string Name, string? ContactPerson, string? Email, string? Phone, string RowVersion)
    : IClientFields;

public sealed class UpdateClientValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientValidator()
    {
        ClientRules.Apply(this);
        RuleFor(x => x.RowVersion).ValidRowVersion();
    }
}

public sealed class UpdateClientHandler(
    IValidator<UpdateClientCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<ClientDto>> HandleAsync(UpdateClientCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var client = await db.Clients.SingleOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (client is null)
        {
            return Error.NotFound("Client");
        }

        var name = command.Name.Trim();
        if (await db.Clients.AnyAsync(c => c.Id != command.Id && c.Name == name, cancellationToken))
        {
            return Error.Conflict("client_name_taken", $"A client named '{name}' already exists.");
        }

        db.ExpectRowVersion(client, command.RowVersion);
        client.Name = name;
        client.ContactPerson = ClientRules.Clean(command.ContactPerson);
        client.Email = ClientRules.Clean(command.Email);
        client.Phone = ClientRules.Clean(command.Phone);
        client.UpdatedAt = clock.UtcNow;
        client.UpdatedById = currentUser.UserId;

        if (await db.TrySaveAsync("This client", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.Clients.AsNoTracking().Where(c => c.Id == client.Id).Select(ClientDto.Projection).SingleAsync(cancellationToken);
    }
}

public sealed record SetClientActiveCommand(int Id, bool IsActive);

/// <summary>Clients are never deleted; inactive clients disappear from pickers but keep their history.</summary>
public sealed class SetClientActiveHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<Result<ClientDto>> HandleAsync(SetClientActiveCommand command, CancellationToken cancellationToken)
    {
        var client = await db.Clients.SingleOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (client is null)
        {
            return Error.NotFound("Client");
        }

        client.IsActive = command.IsActive;
        client.UpdatedAt = clock.UtcNow;
        client.UpdatedById = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);

        return await db.Clients.AsNoTracking().Where(c => c.Id == client.Id).Select(ClientDto.Projection).SingleAsync(cancellationToken);
    }
}
