using QaTracker.Domain.Entities;

namespace QaTracker.Application.Abstractions;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues signed access tokens and opaque refresh tokens (implemented in the API).</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>New random refresh token (URL-safe) – only its hash is stored.</summary>
    string CreateRefreshToken();

    /// <summary>SHA-256 hex hash used to store and look up refresh tokens.</summary>
    string HashRefreshToken(string refreshToken);
}
