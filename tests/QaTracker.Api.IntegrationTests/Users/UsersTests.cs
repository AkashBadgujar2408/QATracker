using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using QaTracker.Api.Endpoints;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Users;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Users;

public sealed class UsersTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object NewUser(string userName, UserRole role = UserRole.Developer) =>
        new { userName, fullName = "Nitin Patil", email = $"{userName}@test.local", role };

    private static string UniqueName(string prefix) => $"{prefix}.{Guid.NewGuid():N}"[..20];

    [Theory]
    [InlineData(UserRole.Tester)]
    [InlineData(UserRole.Developer)]
    public async Task NonAdmins_AreForbidden(UserRole role)
    {
        var client = await api.ClientForAsync(role);

        (await client.GetAsync("/api/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/users", NewUser(UniqueName("x")), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_ReturnsTemporaryPasswordOnce_AndNewUserMustChangeIt()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var userName = UniqueName("nitin");

        var created = await (await admin.PostAsJsonAsync("/api/users", NewUser(userName), Ct))
            .ShouldBeOkAsync<UserWithPasswordDto>(HttpStatusCode.Created);

        created.User.UserName.ShouldBe(userName);
        created.User.MustChangePassword.ShouldBeTrue();
        created.TemporaryPassword.Length.ShouldBe(12);

        var login = await api.CreateBrowser().PostAsJsonAsync("/api/auth/login", new { userName, password = created.TemporaryPassword }, Ct);
        (await login.ShouldBeOkAsync<AuthResponse>()).User.MustChangePassword.ShouldBeTrue();

        var listJson = await admin.GetStringAsync($"/api/users?search={userName}", Ct);
        listJson.ShouldNotContain(created.TemporaryPassword); // passwords are never returned by list/get
    }

    [Fact]
    public async Task Create_DuplicateUserName_Returns409()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var userName = UniqueName("dup");
        await admin.PostAsJsonAsync("/api/users", NewUser(userName), Ct);

        var again = await admin.PostAsJsonAsync("/api/users", new { userName = userName.ToUpperInvariant(), fullName = "X", email = $"other.{userName}@test.local", role = "Tester" }, Ct);

        (await again.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("username_taken");
    }

    [Fact]
    public async Task Create_InvalidUserName_Returns400()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser("has space"), Ct);

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("userName", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Update_WithStaleRowVersion_Returns409()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var created = await (await admin.PostAsJsonAsync("/api/users", NewUser(UniqueName("rv")), Ct))
            .ShouldBeOkAsync<UserWithPasswordDto>(HttpStatusCode.Created);
        var user = created.User;

        var first = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { fullName = "First Edit", user.Email, user.Role, avatarColor = "#0F766E", user.RowVersion }, Ct);
        (await first.ShouldBeOkAsync<UserDto>()).FullName.ShouldBe("First Edit");

        var stale = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { fullName = "Second Edit", user.Email, user.Role, avatarColor = (string?)null, user.RowVersion }, Ct);
        (await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("concurrency");
    }

    [Fact]
    public async Task RoleChange_EndsTheUsersSessions()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var target = await api.CreateUserAsync(UserRole.Developer);
        var (targetClient, _) = await api.LoginAsync(target);
        var dto = (await admin.GetFromJsonAsync<List<UserDto>>($"/api/users?search={target.UserName}", ApiFactory.Json, Ct))!.Single();

        (await admin.PutAsJsonAsync($"/api/users/{dto.Id}", new { dto.FullName, dto.Email, role = "Tester", avatarColor = (string?)null, dto.RowVersion }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await targetClient.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await targetClient.PostAsync("/api/auth/refresh", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResetPassword_IssuesNewTemporaryPassword_AndUnlocks()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var target = await api.CreateUserAsync(UserRole.Tester);

        var reset = await (await admin.PostAsync($"/api/users/{target.Id}/reset-password", null, Ct)).ShouldBeOkAsync<UserWithPasswordDto>();

        reset.User.MustChangePassword.ShouldBeTrue();
        var login = await api.CreateBrowser().PostAsJsonAsync("/api/auth/login", new { target.UserName, password = reset.TemporaryPassword }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.CreateBrowser().PostAsJsonAsync("/api/auth/login", new { target.UserName, password = "Passw0rd!" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivate_BlocksExistingTokenImmediately_AndSelfDeactivationIsRefused()
    {
        var adminUser = await api.CreateUserAsync(UserRole.Admin);
        var (admin, _) = await api.LoginAsync(adminUser);
        var target = await api.CreateUserAsync(UserRole.Developer);
        var (targetClient, _) = await api.LoginAsync(target);

        var deactivated = await (await admin.PatchAsJsonAsync($"/api/users/{target.Id}/active", new { isActive = false }, Ct)).ShouldBeOkAsync<UserDto>();
        deactivated.IsActive.ShouldBeFalse();
        (await targetClient.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var self = await admin.PatchAsJsonAsync($"/api/users/{adminUser.Id}/active", new { isActive = false }, Ct);
        (await self.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("self_deactivation");
    }

    [Fact]
    public async Task List_FiltersByRoleAndActive()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var inactiveTester = await api.CreateUserAsync(UserRole.Tester, isActive: false);

        var users = await admin.GetFromJsonAsync<List<UserDto>>("/api/users?role=Tester&isActive=false", ApiFactory.Json, Ct);

        users!.ShouldContain(u => u.Id == inactiveTester.Id);
        users!.ShouldAllBe(u => u.Role == UserRole.Tester && !u.IsActive);
    }

    [Fact]
    public async Task ExpiredOrForeignToken_IsRejected()
    {
        var client = api.CreateBrowser();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        (await client.GetAsync("/api/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
