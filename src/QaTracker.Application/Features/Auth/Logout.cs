using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Auth;

public sealed record LogoutCommand(string? RefreshToken);

/// <summary>Revokes the current refresh token. Always succeeds (logging out twice is fine).</summary>
public sealed class LogoutHandler(IApplicationDbContext db, ITokenService tokens, IClock clock)
{
    public async Task<Result> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(command.RefreshToken))
        {
            var hash = tokens.HashRefreshToken(command.RefreshToken);
            var now = clock.UtcNow;
            await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);
        }

        return Result.Success();
    }
}
