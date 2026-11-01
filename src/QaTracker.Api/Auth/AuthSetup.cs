using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.Auth;

public static class AuthSetup
{
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddQaAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<ITokenService, TokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = auth.Value.Issuer,
                    ValidAudience = auth.Value.Audience,
                    IssuerSigningKey = TokenService.CreateSigningKey(auth.Value),
                    NameClaimType = QaClaims.UserName,
                    RoleClaimType = QaClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                jwt.Events = new JwtBearerEvents { OnTokenValidated = RejectStaleTokensAsync, OnMessageReceived = ReadHubToken };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.SignedIn, p => p.RequireAuthenticatedUser())
            .AddPolicy(Policies.Authenticated, p => PasswordChanged(p))
            .AddPolicy(Policies.TesterOrAdmin, p => PasswordChanged(p).RequireRole(nameof(UserRole.Tester), nameof(UserRole.Admin)))
            .AddPolicy(Policies.DeveloperOrAdmin, p => PasswordChanged(p).RequireRole(nameof(UserRole.Developer), nameof(UserRole.Admin)))
            .AddPolicy(Policies.AdminOnly, p => PasswordChanged(p).RequireRole(nameof(UserRole.Admin)))
            // Every endpoint requires a signed-in user with a changed password unless it says otherwise.
            .SetDefaultPolicy(PasswordChanged(new AuthorizationPolicyBuilder()).Build())
            .SetFallbackPolicy(PasswordChanged(new AuthorizationPolicyBuilder()).Build());

        var loginPermitLimit = configuration.GetValue("RateLimiting:LoginPermitLimit", 10);
        var apiPermitLimit = configuration.GetValue("RateLimiting:ApiPermitLimit", 600);
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Sign-in and password change: per client address, so passwords cannot be guessed quickly.
            limiter.AddPolicy(LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermitLimit, Window = TimeSpan.FromMinutes(1) }));

            // Everything else: per signed-in user (else per address) – generous, it only stops runaway scripts.
            // The SignalR hub keeps its long-lived connection outside the limit.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.Request.Path.StartsWithSegments("/hubs")
                    ? RateLimitPartition.GetNoLimiter("hub")
                    : RateLimitPartition.GetFixedWindowLimiter(
                        http.User.FindFirst(QaClaims.UserId)?.Value is { } userId
                            ? $"user:{userId}"
                            : $"ip:{http.Connection.RemoteIpAddress}",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = apiPermitLimit, Window = TimeSpan.FromMinutes(1) }));

            limiter.OnRejected = (context, _) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var detail = http.Request.Path.StartsWithSegments("/api/auth")
                    ? "Too many sign-in attempts. Wait a minute and try again."
                    : "Too many requests. Wait a minute and try again.";
                return new ValueTask(TypedResults.Problem(
                        detail: detail,
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests",
                        extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(http));
            };
        });

        return services;
    }

    /// <summary>Browsers cannot set headers on WebSockets: the hub takes the access token from the query string.</summary>
    private static Task ReadHubToken(MessageReceivedContext context)
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/hubs") && context.Request.Query["access_token"] is { Count: 1 } token)
        {
            context.Token = token[0];
        }

        return Task.CompletedTask;
    }

    private static AuthorizationPolicyBuilder PasswordChanged(AuthorizationPolicyBuilder policy) => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => !context.User.HasClaim(c => c.Type == QaClaims.MustChangePassword));

    /// <summary>
    /// Access tokens die as soon as the user is deactivated, the password is changed/reset or the role changes
    /// (all of these replace the security stamp).
    /// </summary>
    private static async Task RejectStaleTokensAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!int.TryParse(principal?.FindFirst(QaClaims.UserId)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            context.Fail("Invalid subject.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user is null || !user.IsActive || user.SecurityStamp != principal!.FindFirst(QaClaims.SecurityStamp)?.Value)
        {
            context.Fail("Session is no longer valid.");
        }
    }
}
