using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;

namespace QaTracker.Api.Auth;

internal sealed class TokenService(IOptions<AuthOptions> options, IClock clock) : ITokenService
{
    public AccessToken CreateAccessToken(User user)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(QaClaims.UserId, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(QaClaims.UserName, user.UserName),
            new(QaClaims.FullName, user.FullName),
            new(QaClaims.Role, user.Role.ToString()),
            new(QaClaims.SecurityStamp, user.SecurityStamp),
        };
        if (user.MustChangePassword)
        {
            claims.Add(new Claim(QaClaims.MustChangePassword, "true"));
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(settings), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expiresAt);
    }

    public string CreateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    public static SymmetricSecurityKey CreateSigningKey(AuthOptions settings) =>
        new(Encoding.UTF8.GetBytes(settings.SigningKey));
}
