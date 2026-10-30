using Microsoft.EntityFrameworkCore;
using QaTracker.Domain.Enums;
using QaTracker.Infrastructure.Persistence;

namespace QaTracker.Api.IntegrationTests.Persistence;

/// <summary>The scaffolded EF Core model builds and maps every enum to its name (no database needed).</summary>
public sealed class ModelTests
{
    private static QaTrackerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<QaTrackerDbContext>()
            .UseSqlServer("Server=model-only;Database=none;Integrated Security=true")
            .Options);

    [Fact]
    public void Model_Builds_WithAllTables()
    {
        using var db = CreateContext();

        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToList();

        tables.Count.ShouldBe(27);
        tables.ShouldContain("Issues");
        tables.ShouldContain("ProjectEnvironments");
        tables.ShouldContain("JobRuns");
        tables.ShouldContain("EmailSettings");
        tables.ShouldNotContain("SchemaVersions");
    }

    [Fact]
    public void EnumProperties_AreStoredAsStrings()
    {
        using var db = CreateContext();

        var enumProperties = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum)
            .ToList();

        enumProperties.Count.ShouldBe(17);
        enumProperties.ShouldAllBe(p => p.GetProviderClrType() == typeof(string));
    }

    [Fact]
    public void IssueStatus_IsTheDomainEnum()
    {
        using var db = CreateContext();

        var status = db.Model.FindEntityType(typeof(Domain.Entities.Issue))!.FindProperty(nameof(Domain.Entities.Issue.Status))!;

        status.ClrType.ShouldBe(typeof(IssueStatus));
    }

    [Fact]
    public void OneTestingSprintPerProject_IsAFilteredUniqueIndex()
    {
        using var db = CreateContext();

        var index = db.Model.FindEntityType(typeof(Domain.Entities.TestingSprint))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == "UX_TestingSprints_OneTestingPerProject");

        index.IsUnique.ShouldBeTrue();
        index.GetFilter().ShouldBe("([Status]=N'Testing')");
    }
}
