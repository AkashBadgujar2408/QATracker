using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Auth;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Users;

/// <param name="RowVersion">From the UserDto the client edited (optimistic concurrency → 409).</param>
public sealed record UpdateUserCommand(int Id, string FullName, string Email, UserRole Role, string? AvatarColor, string RowVersion);

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        UserRules.FullNameEmailRoleColor(this, x => x.FullName, x => x.Email, x => x.Role, x => x.AvatarColor);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

/// <summary>Admin edits a user. The user name cannot change. A role change ends the user's sessions.</summary>
public sealed class UpdateUserHandler(IValidator<UpdateUserCommand> validator, IApplicationDbContext db, IClock clock, AuthSessions sessions)
{
    public async Task<Result<UserDto>> HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.Id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("User");
        }

        var email = command.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Id != command.Id && u.Email == email, cancellationToken))
        {
            return Error.Conflict("email_taken", $"E-mail '{email}' is already used by another user.");
        }

        var roleChanged = user.Role != command.Role;
        if (roleChanged && await UserRules.IsLastActiveAdminAsync(db, user.Id, cancellationToken))
        {
            return Error.Rule("last_admin", "The last active admin must keep the Admin role.");
        }

        db.Entry(user).Property(u => u.RowVersion).OriginalValue = Convert.FromBase64String(command.RowVersion);
        user.FullName = command.FullName.Trim();
        user.Email = email;
        user.Role = command.Role;
        user.AvatarColor = string.IsNullOrWhiteSpace(command.AvatarColor) ? null : command.AvatarColor;
        user.UpdatedAt = clock.UtcNow;

        if (roleChanged)
        {
            AuthSessions.InvalidateTokens(user);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Concurrency("This user");
        }

        if (roleChanged)
        {
            await sessions.RevokeAllAsync(user.Id, cancellationToken);
        }

        return UserDto.From(user);
    }
}
