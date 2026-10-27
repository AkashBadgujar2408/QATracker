using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Users;

public sealed record CreateUserCommand(string UserName, string FullName, string Email, UserRole Role, string? AvatarColor);

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().Matches(UserRules.UserNamePattern)
            .WithMessage("User name must be 3–50 characters: letters, digits, '.', '_' or '-'.");
        UserRules.FullNameEmailRoleColor(this, x => x.FullName, x => x.Email, x => x.Role, x => x.AvatarColor);
    }
}

/// <summary>Admin creates a user with a generated temporary password (must be changed at first login).</summary>
public sealed class CreateUserHandler(IValidator<CreateUserCommand> validator, IApplicationDbContext db, IClock clock)
{
    public async Task<Result<UserWithPasswordDto>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var userName = command.UserName.Trim();
        var email = command.Email.Trim();

        if (await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
        {
            return Error.Conflict("username_taken", $"User name '{userName}' is already taken.");
        }

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return Error.Conflict("email_taken", $"E-mail '{email}' is already used by another user.");
        }

        var temporaryPassword = PasswordRules.GenerateTemporary();
        var user = new User
        {
            UserName = userName,
            FullName = command.FullName.Trim(),
            Email = email,
            Password = temporaryPassword,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = command.Role,
            IsActive = true,
            MustChangePassword = true,
            AvatarColor = string.IsNullOrWhiteSpace(command.AvatarColor) ? null : command.AvatarColor,
            CreatedAt = clock.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new UserWithPasswordDto(UserDto.From(user), temporaryPassword);
    }
}
