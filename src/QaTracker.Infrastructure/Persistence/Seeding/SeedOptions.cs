using System.ComponentModel.DataAnnotations;

namespace QaTracker.Infrastructure.Persistence.Seeding;

/// <summary>
/// "Seed" configuration section. The admin password is a secret: set it with
/// <c>dotnet user-secrets set "Seed:AdminPassword" "..." --project src/QaTracker.Api</c> or the environment
/// variable <c>Seed__AdminPassword</c> – never in appsettings.json.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    [Required, RegularExpression("^[A-Za-z0-9._-]{3,50}$")]
    public string AdminUserName { get; set; } = "admin";

    [Required, EmailAddress]
    public string AdminEmail { get; set; } = "admin@qatracker.local";

    [Required, MaxLength(100)]
    public string AdminFullName { get; set; } = "Administrator";

    /// <summary>Initial admin password (the admin must change it on first login). No admin is created when empty.</summary>
    public string? AdminPassword { get; set; }
}
