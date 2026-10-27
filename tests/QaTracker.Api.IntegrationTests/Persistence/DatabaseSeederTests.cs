using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Enums;
using QaTracker.Infrastructure.Persistence;
using QaTracker.Infrastructure.Persistence.Seeding;

namespace QaTracker.Api.IntegrationTests.Persistence;

/// <summary>Runs against a throw-away database built from database/scripts (requires local SQL Server).</summary>
public sealed class DatabaseSeederTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
    }

    private static DatabaseSeeder CreateSeeder(QaTrackerDbContext db, string? adminPassword) =>
        new(
            db,
            new FixedClock(),
            Options.Create(new SeedOptions { AdminUserName = "admin", AdminPassword = adminPassword }),
            NullLogger<DatabaseSeeder>.Instance);

    [Fact]
    public async Task Seed_TwiceWithAdminPassword_CreatesReferenceDataOnce()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = database.CreateDbContext())
        {
            await CreateSeeder(db, "Initial#123").SeedAsync(ct);
        }

        await using (var db = database.CreateDbContext())
        {
            await CreateSeeder(db, "Initial#123").SeedAsync(ct);
        }

        await using var check = database.CreateDbContext();
        (await check.SprintPhases.OrderBy(p => p.SortOrder).Select(p => p.Name).ToListAsync(ct))
            .ShouldBe(DatabaseSeeder.DefaultPhases);
        (await check.SlaPolicies.CountAsync(ct)).ShouldBe(5);
        (await check.SlaPolicies.SingleAsync(p => p.Severity == Severity.NotAssessed, ct)).WorkingHours.ShouldBe(280);
        (await check.SlaPolicies.SingleAsync(p => p.Severity == Severity.Critical, ct)).WorkingHours.ShouldBe(56);

        var admin = await check.Users.SingleAsync(ct);
        admin.UserName.ShouldBe("admin");
        admin.Role.ShouldBe(UserRole.Admin);
        admin.Password.ShouldBe("Initial#123");
        admin.MustChangePassword.ShouldBeTrue();
        admin.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Seed_StoresEnumNames_AcceptedByCheckConstraints()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = database.CreateDbContext())
        {
            await CreateSeeder(db, "Initial#123").SeedAsync(ct);
        }

        await using var check = database.CreateDbContext();
        var roles = await check.Database
            .SqlQuery<string>($"SELECT Role AS Value FROM dbo.Users")
            .ToListAsync(ct);

        roles.ShouldBe(["Admin"]);
    }
}
