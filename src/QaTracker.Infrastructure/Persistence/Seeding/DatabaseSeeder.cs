using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Infrastructure.Persistence.Seeding;

/// <summary>
/// Idempotent seed of reference data: sprint phases, SLA defaults and the first admin user.
/// Only adds what is missing – never changes rows an admin may have edited.
/// </summary>
public sealed partial class DatabaseSeeder(
    QaTrackerDbContext db,
    IClock clock,
    IOptions<SeedOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>Plan 3 / 12: default sprint phases in display order.</summary>
    public static readonly IReadOnlyList<string> DefaultPhases = ["Early Testing", "UAT", "LIVE", "Regression", "Other"];

    /// <summary>
    /// Plan 8.3: Critical 1, High 3, Medium 7, Low 15 working days (8 working hours per day); Not assessed 30 working days
    /// so unclassified issues still become overdue (ADR-016). Admin can change all of them (S28).
    /// </summary>
    public static readonly IReadOnlyDictionary<Severity, int> DefaultSlaWorkingHours = new Dictionary<Severity, int>
    {
        [Severity.Critical] = 1 * 8,
        [Severity.High] = 3 * 8,
        [Severity.Medium] = 7 * 8,
        [Severity.Low] = 15 * 8,
        [Severity.NotAssessed] = 30 * 8,
    };

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedPhasesAsync(cancellationToken);
        await SeedSlaPoliciesAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedPhasesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.SprintPhases.Select(p => p.Name).ToListAsync(cancellationToken);
        var nextSortOrder = await db.SprintPhases.MaxAsync(p => (int?)p.SortOrder, cancellationToken) ?? 0;

        foreach (var name in DefaultPhases.Where(name => !existing.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            db.SprintPhases.Add(new SprintPhase { Name = name, SortOrder = ++nextSortOrder, IsActive = true });
        }
    }

    private async Task SeedSlaPoliciesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.SlaPolicies.Select(p => p.Severity).ToListAsync(cancellationToken);

        foreach (var (severity, hours) in DefaultSlaWorkingHours.Where(sla => !existing.Contains(sla.Key)))
        {
            db.SlaPolicies.Add(new SlaPolicy { Severity = severity, WorkingHours = hours });
        }
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken))
        {
            return;
        }

        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            LogAdminNotSeeded(logger);
            return;
        }

        var admin = new User
        {
            UserName = seed.AdminUserName,
            FullName = seed.AdminFullName,
            Email = seed.AdminEmail,
            Password = seed.AdminPassword,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Admin,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = clock.UtcNow,
        };
        db.Users.Add(admin);
        LogAdminSeeded(logger, seed.AdminUserName);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No admin user exists and Seed:AdminPassword is not set – set it with dotnet user-secrets to create the first admin.")]
    private static partial void LogAdminNotSeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded admin user {AdminUserName} (must change password on first login).")]
    private static partial void LogAdminSeeded(ILogger logger, string adminUserName);
}
