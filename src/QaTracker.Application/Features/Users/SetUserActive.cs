using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Auth;

namespace QaTracker.Application.Features.Users;

public sealed record SetUserActiveCommand(int Id, bool IsActive);

/// <summary>Activate / deactivate (users are never deleted). Deactivation ends the user's sessions immediately.</summary>
public sealed class SetUserActiveHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock, AuthSessions sessions)
{
    public async Task<Result<UserDto>> HandleAsync(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.Id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("User");
        }

        if (user.IsActive == command.IsActive)
        {
            return UserDto.From(user);
        }

        if (!command.IsActive)
        {
            if (user.Id == currentUser.UserId)
            {
                return Error.Rule("self_deactivation", "You cannot deactivate your own account.");
            }

            if (await UserRules.IsLastActiveAdminAsync(db, user.Id, cancellationToken))
            {
                return Error.Rule("last_admin", "The last active admin cannot be deactivated.");
            }

            AuthSessions.InvalidateTokens(user);
            await sessions.RevokeAllAsync(user.Id, cancellationToken);
        }

        user.IsActive = command.IsActive;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return UserDto.From(user);
    }
}
