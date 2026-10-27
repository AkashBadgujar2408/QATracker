using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Auth;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).ValidPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}

/// <summary>Own password change (also the forced change after a temporary password). Ends all other sessions.</summary>
public sealed class ChangePasswordHandler(IValidator<ChangePasswordCommand> validator, IApplicationDbContext db, ICurrentUser currentUser, IClock clock, AuthSessions sessions)
{
    public async Task<Result<AuthResult>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("User");
        }

        if (!PasswordRules.Matches(user.Password, command.CurrentPassword))
        {
            return Error.Validation(nameof(command.CurrentPassword), "Current password is incorrect.");
        }

        user.Password = command.NewPassword;
        user.MustChangePassword = false;
        user.UpdatedAt = clock.UtcNow;
        AuthSessions.InvalidateTokens(user);
        await sessions.RevokeAllAsync(user.Id, cancellationToken);

        return await sessions.IssueAsync(user, cancellationToken);
    }
}
