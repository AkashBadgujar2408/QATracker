using System.ComponentModel.DataAnnotations;

namespace QaTracker.Application.Common;

/// <summary>"Auth" configuration section. <see cref="SigningKey"/> is a secret (user-secrets / environment only).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Required]
    public string Issuer { get; set; } = "qa-tracker";

    [Required]
    public string Audience { get; set; } = "qa-tracker";

    /// <summary>HMAC-SHA256 key, at least 32 characters.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;

    [Range(1, 100)]
    public int MaxFailedLogins { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}
