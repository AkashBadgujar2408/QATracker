using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QaTracker.Api.Endpoints;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Infrastructure.Persistence;
using QaTracker.Infrastructure.Persistence.Seeding;

[assembly: AssemblyFixture(typeof(QaTracker.Api.IntegrationTests.Infrastructure.ApiFactory))]

namespace QaTracker.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The real API on a throw-away database (one per test run, shared by all test classes – use unique names).
/// Clients use https://localhost so the Secure refresh cookie is stored and sent.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestSigningKey = "integration-tests-signing-key-0123456789abcdef";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TestDatabase _database = new();

    /// <summary>Attachment files of this test run (deleted afterwards).</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "qa-tracker-tests", Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();

        // Reference data (sprint phases, SLA defaults) as on a real start-up; no admin account.
        await using var db = _database.CreateDbContext();
        await new DatabaseSeeder(db, new UtcClock(), Options.Create(new SeedOptions()), NullLogger<DatabaseSeeder>.Instance)
            .SeedAsync(CancellationToken.None);
    }

    private sealed class UtcClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:QaTracker", _database.ConnectionString);
        builder.UseSetting("Auth:SigningKey", TestSigningKey);
        builder.UseSetting("Database:SeedOnStartup", "false");
        builder.UseSetting("Storage:Root", StorageRoot);
        builder.UseSetting("RateLimiting:LoginPermitLimit", "10000");
        builder.UseSetting("RateLimiting:ApiPermitLimit", "1000000");
        // Mails are queued (and checked by the tests) but not sent; the SMTP port is closed on purpose.
        builder.UseSetting("Email:Enabled", "true");
        builder.UseSetting("Email:Workers", "false");
        builder.UseSetting("Email:Smtp:Host", "127.0.0.1");
        builder.UseSetting("Email:Smtp:Port", "1");
    }

    /// <summary>Client that keeps cookies (refresh token) – one per simulated browser.</summary>
    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    public QaTrackerDbContext CreateDbContext() => _database.CreateDbContext();

    /// <summary>Inserts a user directly (password stored as given) and returns it.</summary>
    public async Task<User> CreateUserAsync(UserRole role, string password = "Passw0rd!", bool mustChangePassword = false, bool isActive = true)
    {
        var userName = $"{role.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}"[..30];
        var user = new User
        {
            UserName = userName,
            FullName = $"Test {role}",
            Email = $"{userName}@test.local",
            Password = password,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = role,
            IsActive = isActive,
            MustChangePassword = mustChangePassword,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var db = CreateDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Logs in with a new browser; the returned client sends the access token on every request.</summary>
    public async Task<(HttpClient Client, AuthResponse Session)> LoginAsync(User user, string password = "Passw0rd!")
    {
        var client = CreateBrowser();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.UserName, password });
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return (client, session);
    }

    /// <summary>Shortcut: new user of the role, logged in.</summary>
    public async Task<HttpClient> ClientForAsync(UserRole role) => (await LoginAsync(await CreateUserAsync(role))).Client;

    public T Service<T>()
        where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();
}
