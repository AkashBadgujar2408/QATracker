using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Auth;

namespace QaTracker.Api.Endpoints;

/// <summary>Access token in the response body; refresh token only in the httpOnly cookie.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, CurrentUserDto User);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginCommand command, LoginHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(session => SignIn(http, session)))
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.LoginRateLimitPolicy)
            .WithSummary("Sign in with user name and password.");

        group.MapPost("/refresh", async (RefreshHandler handler, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(new RefreshCommand(http.Request.Cookies[RefreshCookie.Name]), ct);
                if (!result.IsSuccess)
                {
                    DeleteRefreshCookie(http);
                }

                return result.ToHttp(session => SignIn(http, session));
            })
            .AllowAnonymous()
            .WithSummary("New access token from the refresh cookie (rotates the cookie).");

        group.MapPost("/logout", async (LogoutHandler handler, HttpContext http, CancellationToken ct) =>
            {
                await handler.HandleAsync(new LogoutCommand(http.Request.Cookies[RefreshCookie.Name]), ct);
                DeleteRefreshCookie(http);
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .WithSummary("End the current session.");

        group.MapGet("/me", async (GetMeHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttp(user => TypedResults.Ok(user)))
            .RequireAuthorization(Policies.SignedIn)
            .WithSummary("The signed-in user.");

        group.MapPost("/change-password", async (ChangePasswordCommand command, ChangePasswordHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(command, http.Request.Cookies[RefreshCookie.Name], ct)).ToHttp(session => SignIn(http, session)))
            .RequireAuthorization(Policies.SignedIn)
            .WithSummary("Change own password (also completes a forced change). Returns a fresh session.");

        return app;
    }

    private static IResult SignIn(HttpContext http, AuthResult session)
    {
        http.Response.Cookies.Append(RefreshCookie.Name, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookie.Path,
            // No Expires = browser-session cookie ("remember me" off).
            Expires = session.IsPersistent ? session.RefreshTokenExpiresAt : null,
            IsEssential = true,
        });
        return TypedResults.Ok(new AuthResponse(session.AccessToken.Token, session.AccessToken.ExpiresAt, session.User));
    }

    private static void DeleteRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshCookie.Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookie.Path,
        });
}
