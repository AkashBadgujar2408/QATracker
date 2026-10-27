using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QaTracker.Infrastructure.Persistence;

namespace QaTracker.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Throw-away database on the local SQL Server (ADR-003): created from database/scripts, dropped after the tests.
/// Server comes from the QATRACKER_TEST_SQLSERVER environment variable (default <c>.\SQLEXPRESS</c>, Windows auth).
/// </summary>
public sealed partial class TestDatabase : IAsyncLifetime
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("QATRACKER_TEST_SQLSERVER") ?? @".\SQLEXPRESS";

    public string Name { get; } = $"QaTracker_Test_{Guid.NewGuid():N}";

    public string ConnectionString => BuildConnectionString(Name);

    public QaTrackerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<QaTrackerDbContext>().UseSqlServer(ConnectionString).Options);

    public async ValueTask InitializeAsync()
    {
        await ExecuteAsync(BuildConnectionString("master"), $"CREATE DATABASE [{Name}];");

        foreach (var script in SchemaScripts())
        {
            foreach (var batch in GoSeparator().Split(await File.ReadAllTextAsync(script)))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await ExecuteAsync(ConnectionString, batch);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(
            BuildConnectionString("master"),
            $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}];");
    }

    /// <summary>database/scripts/NNN_*.sql in order, without 000_CreateDatabase.sql.</summary>
    public static IEnumerable<string> SchemaScripts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QaTracker.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Repository root (QaTracker.sln) not found.");
        }

        return Directory.GetFiles(Path.Combine(directory.FullName, "database", "scripts"), "*.sql")
            .Where(path => ScriptName().IsMatch(Path.GetFileName(path)) && !path.EndsWith("000_CreateDatabase.sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    private static string BuildConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = Server,
        InitialCatalog = database,
        IntegratedSecurity = true,
        TrustServerCertificate = true,
    }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();

    [GeneratedRegex(@"^\d{3}_.+\.sql$")]
    private static partial Regex ScriptName();
}
