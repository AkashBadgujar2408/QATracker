using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Auth;

/// <summary>The signed-in user as the client sees it (never contains the password).</summary>
public sealed record CurrentUserDto(
    int Id,
    string UserName,
    string FullName,
    string Email,
    UserRole Role,
    bool MustChangePassword,
    string? AvatarColor)
{
    public static CurrentUserDto From(User user) =>
        new(user.Id, user.UserName, user.FullName, user.Email, user.Role, user.MustChangePassword, user.AvatarColor);
}

/// <summary>New session: access token for the response body, refresh token for the httpOnly cookie.</summary>
public sealed record AuthResult(
    AccessToken AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserDto User);

/// <summary>Creates and revokes sessions (refresh tokens rotate on every use).</summary>
public sealed class AuthSessions(
    IApplicationDbContext db,
    ITokenService tokens,
    IClock clock,
    ICurrentUser currentUser,
    IOptions<AuthOptions> options)
{
    /// <summary>Stores a new refresh token for <paramref name="user"/>, saves all pending changes and returns the session.</summary>
    public async Task<AuthResult> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var refreshToken = tokens.CreateRefreshToken();
        var expiresAt = now.AddDays(options.Value.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashRefreshToken(refreshToken),
            CreatedAt = now,
            ExpiresAt = expiresAt,
            CreatedByIp = currentUser.IpAddress,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(tokens.CreateAccessToken(user), refreshToken, expiresAt, CurrentUserDto.From(user));
    }

    /// <summary>Ends every session of the user (password change/reset, deactivation, role change, token reuse).</summary>
    public Task RevokeAllAsync(int userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    /// <summary>New security stamp: access tokens issued before this moment stop working on their next request.</summary>
    public static void InvalidateTokens(User user) => user.SecurityStamp = Guid.NewGuid().ToString("N");
}
