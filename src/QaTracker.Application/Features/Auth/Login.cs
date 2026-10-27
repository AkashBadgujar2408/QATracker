using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Auth;

/// <param name="RememberMe">Keep the session after the browser is closed (persistent refresh cookie).</param>
public sealed record LoginCommand(string UserName, string Password, bool RememberMe = false);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

/// <summary>User name + password login with lockout after repeated failures (ADR-011).</summary>
public sealed class LoginHandler(
    IValidator<LoginCommand> validator,
    IApplicationDbContext db,
    IClock clock,
    IOptions<AuthOptions> options,
    AuthSessions sessions)
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("invalid_credentials", "Invalid user name or password.");

    public async Task<Result<AuthResult>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var userName = command.UserName.Trim();
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserName == userName, cancellationToken);
        if (user is null)
        {
            return InvalidCredentials;
        }

        var now = clock.UtcNow;
        if (user.LockoutEndAt > now)
        {
            var minutes = (int)Math.Ceiling((user.LockoutEndAt.Value - now).TotalMinutes);
            return Error.Unauthorized("locked_out", $"Too many failed attempts. Try again in {minutes} minute(s).");
        }

        if (!PasswordRules.Matches(user.Password, command.Password))
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= options.Value.MaxFailedLogins)
            {
                user.AccessFailedCount = 0;
                user.LockoutEndAt = now.AddMinutes(options.Value.LockoutMinutes);
            }

            await db.SaveChangesAsync(cancellationToken);
            return InvalidCredentials;
        }

        if (!user.IsActive)
        {
            return Error.Unauthorized("inactive", "This account is deactivated. Contact an administrator.");
        }

        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = now;
        return await sessions.IssueAsync(user, command.RememberMe, cancellationToken);
    }
}
