using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Auth;

namespace QaTracker.Application.Features.Users;

public sealed record ResetPasswordCommand(int Id);

/// <summary>Admin sets a new temporary password; the user must change it at next login. Also unlocks the account.</summary>
public sealed class ResetPasswordHandler(IApplicationDbContext db, IClock clock, AuthSessions sessions)
{
    public async Task<Result<UserWithPasswordDto>> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.Id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("User");
        }

        var temporaryPassword = PasswordRules.GenerateTemporary();
        user.Password = temporaryPassword;
        user.MustChangePassword = true;
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.UpdatedAt = clock.UtcNow;
        AuthSessions.InvalidateTokens(user);

        await sessions.RevokeAllAsync(user.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new UserWithPasswordDto(UserDto.From(user), temporaryPassword);
    }
}
