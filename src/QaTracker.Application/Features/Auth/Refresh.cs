using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Auth;

/// <param name="RefreshToken">Value of the refresh cookie (null when absent).</param>
public sealed record RefreshCommand(string? RefreshToken);

/// <summary>Exchanges a refresh token for a new session; the old token is revoked (rotation).</summary>
public sealed class RefreshHandler(IApplicationDbContext db, ITokenService tokens, IClock clock, AuthSessions sessions)
{
    private static readonly Error NoSession = Error.Unauthorized("no_session", "Your session has ended. Please sign in again.");

    public async Task<Result<AuthResult>> HandleAsync(RefreshCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            return NoSession;
        }

        var hash = tokens.HashRefreshToken(command.RefreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            return NoSession;
        }

        if (stored.RevokedAt is not null)
        {
            // A rotated-away token was replayed: assume it was stolen and end every session of the user.
            await sessions.RevokeAllAsync(stored.UserId, cancellationToken);
            return NoSession;
        }

        var now = clock.UtcNow;
        if (stored.ExpiresAt <= now || !stored.User.IsActive)
        {
            return NoSession;
        }

        stored.RevokedAt = now;
        return await sessions.IssueAsync(stored.User, cancellationToken);
    }
}
