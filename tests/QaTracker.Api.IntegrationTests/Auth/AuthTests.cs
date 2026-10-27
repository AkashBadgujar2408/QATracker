using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QaTracker.Api.Endpoints;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Auth;

public sealed class AuthTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_WithUserName_ReturnsAccessTokenAndHttpOnlyRefreshCookie()
    {
        var user = await api.CreateUserAsync(UserRole.Tester);
        var client = api.CreateBrowser();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "Passw0rd!" }, Ct);

        var session = await response.ShouldBeOkAsync<AuthResponse>();
        session.AccessToken.ShouldNotBeNullOrEmpty();
        session.User.UserName.ShouldBe(user.UserName);
        session.User.Role.ShouldBe(UserRole.Tester);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qa_refresh=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/auth", Case.Insensitive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_RememberMe_ControlsCookieLifetime_AndSurvivesRotation(bool rememberMe)
    {
        var user = await api.CreateUserAsync(UserRole.Tester);
        var browser = api.CreateBrowser();

        var login = await browser.PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "Passw0rd!", rememberMe }, Ct);
        var refreshed = await browser.PostAsync("/api/auth/refresh", null, Ct);

        foreach (var response in new[] { login, refreshed })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qa_refresh=", StringComparison.Ordinal));
            cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase).ShouldBe(rememberMe);
        }
    }

    [Fact]
    public async Task Login_UserNameIsCaseInsensitive_AndEmailIsNotAccepted()
    {
        var user = await api.CreateUserAsync(UserRole.Developer);
        var client = api.CreateBrowser();

        (await client.PostAsJsonAsync("/api/auth/login", new { userName = user.UserName.ToUpperInvariant(), password = "Passw0rd!" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var byEmail = await client.PostAsJsonAsync("/api/auth/login", new { userName = user.Email, password = "Passw0rd!" }, Ct);
        (await byEmail.ShouldBeProblemAsync(HttpStatusCode.Unauthorized)).Code().ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Login_WrongPassword_LocksOutAfterFiveAttempts()
    {
        var user = await api.CreateUserAsync(UserRole.Tester);
        var client = api.CreateBrowser();

        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "Wrong123" }, Ct);
            (await wrong.ShouldBeProblemAsync(HttpStatusCode.Unauthorized)).Code().ShouldBe("invalid_credentials");
        }

        var correct = await client.PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "Passw0rd!" }, Ct);
        (await correct.ShouldBeProblemAsync(HttpStatusCode.Unauthorized)).Code().ShouldBe("locked_out");
    }

    [Fact]
    public async Task Login_Validation_Returns400WithCamelCaseFields()
    {
        var response = await api.CreateBrowser().PostAsJsonAsync("/api/auth/login", new { userName = "", password = "" }, Ct);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("userName", out _).ShouldBeTrue();
        problem.GetProperty("errors").TryGetProperty("password", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Login_InactiveUser_IsRejected()
    {
        var user = await api.CreateUserAsync(UserRole.Developer, isActive: false);

        var response = await api.CreateBrowser().PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "Passw0rd!" }, Ct);

        (await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized)).Code().ShouldBe("inactive");
    }

    [Fact]
    public async Task Refresh_UsesCookie_RotatesIt_AndReplayRevokesAllSessions()
    {
        var user = await api.CreateUserAsync(UserRole.Tester);
        var browser = api.CreateBrowser();
        var login = await browser.PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "Passw0rd!" }, Ct);
        var firstToken = RefreshCookieValue(login);

        var refreshed = await browser.PostAsync("/api/auth/refresh", null, Ct);
        (await refreshed.ShouldBeOkAsync<AuthResponse>()).User.Id.ShouldBe(user.Id);
        RefreshCookieValue(refreshed).ShouldNotBe(firstToken);
        (await ActiveRefreshTokensAsync(user.Id)).ShouldBe(1);

        // An attacker replays the first (rotated-away) token: every session of the user ends.
        using var attacker = api.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", $"qa_refresh={firstToken}");
        (await attacker.SendAsync(replay, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await ActiveRefreshTokensAsync(user.Id)).ShouldBe(0);
        (await browser.PostAsync("/api/auth/refresh", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string RefreshCookieValue(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("qa_refresh=", StringComparison.Ordinal))
            .Split(';')[0]["qa_refresh=".Length..];

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        var response = await api.CreateBrowser().PostAsync("/api/auth/refresh", null, Ct);
        (await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized)).Code().ShouldBe("no_session");
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (client, _) = await api.LoginAsync(await api.CreateUserAsync(UserRole.Tester));

        (await client.PostAsync("/api/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsync("/api/auth/refresh", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401_WithToken_ReturnsUser()
    {
        (await api.CreateBrowser().GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var user = await api.CreateUserAsync(UserRole.Developer);
        var (client, _) = await api.LoginAsync(user);
        var me = await (await client.GetAsync("/api/auth/me", Ct)).ShouldBeOkAsync<Application.Features.Auth.CurrentUserDto>();
        me.UserName.ShouldBe(user.UserName);
    }

    [Fact]
    public async Task MustChangePassword_OnlyAllowsMeAndChangePassword_UntilChanged()
    {
        var user = await api.CreateUserAsync(UserRole.Admin, password: "Temp1234x", mustChangePassword: true);
        var (client, session) = await api.LoginAsync(user, "Temp1234x");
        session.User.MustChangePassword.ShouldBeTrue();

        (await client.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var changed = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Temp1234x", newPassword = "MyNewPass9" }, Ct);
        var newSession = await changed.ShouldBeOkAsync<AuthResponse>();
        newSession.User.MustChangePassword.ShouldBeFalse();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newSession.AccessToken);
        (await client.GetAsync("/api/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.CreateDbContext();
        (await db.Users.SingleAsync(u => u.Id == user.Id, Ct)).Password.ShouldBe("MyNewPass9");
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentOrWeakNew_Returns400()
    {
        var (client, _) = await api.LoginAsync(await api.CreateUserAsync(UserRole.Tester));

        var wrongCurrent = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "nope", newPassword = "Another123" }, Ct);
        (await wrongCurrent.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("currentPassword", out _).ShouldBeTrue();

        var weak = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Passw0rd!", newPassword = "short" }, Ct);
        (await weak.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("newPassword", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task OldAccessToken_StopsWorking_AfterPasswordChange()
    {
        var (client, session) = await api.LoginAsync(await api.CreateUserAsync(UserRole.Tester));

        await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Passw0rd!", newPassword = "Changed123" }, Ct);

        var stale = api.CreateBrowser();
        stale.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        (await stale.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<int> ActiveRefreshTokensAsync(int userId)
    {
        await using var db = api.CreateDbContext();
        return await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null, Ct);
    }
}
