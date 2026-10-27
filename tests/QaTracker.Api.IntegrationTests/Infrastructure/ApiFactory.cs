using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Api.Endpoints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Infrastructure.Persistence;

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

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:QaTracker", _database.ConnectionString);
        builder.UseSetting("Auth:SigningKey", TestSigningKey);
        builder.UseSetting("Database:SeedOnStartup", "false");
        builder.UseSetting("RateLimiting:LoginPermitLimit", "10000");
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
