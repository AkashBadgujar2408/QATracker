using System.Net;
using System.Net.Http.Json;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Environments;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Masters;

public sealed class MastersTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];

    /// <summary>Unique valid project key, e.g. "K3F9A1B2".</summary>
    private static string UniqueKey() => "K" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    private async Task<(HttpClient Tester, ClientDto Client, ProjectDto Project)> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = Unique("Pardi Gears") }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key = UniqueKey() }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        return (tester, client, project);
    }

    [Fact]
    public async Task Tester_BuildsTheWholeHierarchy_AndDeveloperCanRead()
    {
        var (tester, client, project) = await SeedAsync();

        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales & Distribution" }, Ct))
            .ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var environment = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.client.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var developer = await api.CreateUserAsync(UserRole.Developer);
        var members = await (await tester.PutAsJsonAsync($"/api/projects/{project.Id}/members", new { userIds = new[] { developer.Id } }, Ct))
            .ShouldBeOkAsync<List<MemberDto>>();

        module.Name.ShouldBe("Sales & Distribution");
        environment.Type.ShouldBe(EnvironmentType.UAT);
        members.Single().UserId.ShouldBe(developer.Id);

        var (devClient, _) = await api.LoginAsync(developer);
        var cascade = await (await devClient.GetAsync($"/api/lookups/cascade?clientId={client.Id}&projectId={project.Id}", Ct))
            .ShouldBeOkAsync<CascadeDto>();
        cascade.Clients.ShouldContain(c => c.Id == client.Id);
        cascade.Projects.Single().Key.ShouldBe(project.Key);
        cascade.Modules.Single().Name.ShouldBe("Sales & Distribution");
        cascade.Environments.Single().Url.ShouldBe("https://uat.client.com");
        cascade.Members.Single().UserId.ShouldBe(developer.Id);

        var fetched = await (await devClient.GetAsync($"/api/projects/{project.Id}", Ct)).ShouldBeOkAsync<ProjectDto>();
        (fetched.ModuleCount, fetched.EnvironmentCount, fetched.MemberCount).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task Developer_CannotCreateOrEdit()
    {
        var (_, client, project) = await SeedAsync();
        var developer = await api.ClientForAsync(UserRole.Developer);

        (await developer.PostAsJsonAsync("/api/clients", new { name = Unique("Dev Client") }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await developer.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "X", key = UniqueKey() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await developer.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Kaizen" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await developer.PatchAsJsonAsync($"/api/clients/{client.Id}/active", new { isActive = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await developer.GetAsync($"/api/clients/{client.Id}/projects", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Names_AreUniqueInTheirScope()
    {
        var (tester, client, project) = await SeedAsync();

        var duplicateClient = await tester.PostAsJsonAsync("/api/clients", new { name = client.Name.ToUpperInvariant() }, Ct);
        (await duplicateClient.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("client_name_taken");

        var duplicateProject = await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "erp", key = UniqueKey() }, Ct);
        (await duplicateProject.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("project_name_taken");

        await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Kaizen" }, Ct);
        var duplicateModule = await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = " kaizen " }, Ct);
        (await duplicateModule.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("module_name_taken");

        // Same module name in another project is fine.
        var (_, _, otherProject) = await SeedAsync();
        (await tester.PostAsJsonAsync($"/api/projects/{otherProject.Id}/modules", new { name = "Kaizen" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ProjectKey_IsValidated_NormalizedAndGloballyUnique()
    {
        var (tester, client, project) = await SeedAsync();

        var invalid = await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "Bad", key = "1-bad" }, Ct);
        (await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("key", out _).ShouldBeTrue();

        var (_, otherClient, _) = await SeedAsync();
        var taken = await tester.PostAsJsonAsync($"/api/clients/{otherClient.Id}/projects", new { name = "Other", key = project.Key.ToLowerInvariant() }, Ct);
        (await taken.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("project_key_taken");

        var lower = UniqueKey().ToLowerInvariant();
        var created = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "Portal", key = $" {lower} " }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        created.Key.ShouldBe(lower.ToUpperInvariant());
    }

    [Fact]
    public async Task Update_WithStaleRowVersion_Returns409()
    {
        var (tester, client, _) = await SeedAsync();

        var first = await tester.PutAsJsonAsync($"/api/clients/{client.Id}", new { name = client.Name, contactPerson = "John", email = "john@client.com", phone = (string?)null, client.RowVersion }, Ct);
        (await first.ShouldBeOkAsync<ClientDto>()).ContactPerson.ShouldBe("John");

        var stale = await tester.PutAsJsonAsync($"/api/clients/{client.Id}", new { name = client.Name, contactPerson = "Emily", email = (string?)null, phone = (string?)null, client.RowVersion }, Ct);
        (await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("concurrency");
    }

    [Fact]
    public async Task InactiveClient_HidesFromCascade_AndBlocksNewProjects()
    {
        var (tester, client, _) = await SeedAsync();

        (await tester.PatchAsJsonAsync($"/api/clients/{client.Id}/active", new { isActive = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var cascade = await (await tester.GetAsync("/api/lookups/cascade", Ct)).ShouldBeOkAsync<CascadeDto>();
        cascade.Clients.ShouldNotContain(c => c.Id == client.Id);
        var blocked = await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "New", key = UniqueKey() }, Ct);
        (await blocked.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("client_inactive");

        var all = await (await tester.GetAsync("/api/clients?includeInactive=true", Ct)).ShouldBeOkAsync<List<ClientDto>>();
        all.ShouldContain(c => c.Id == client.Id && !c.IsActive);
    }

    [Fact]
    public async Task Environment_RequiresHttpUrl()
    {
        var (tester, _, project) = await SeedAsync();

        var response = await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "LIVE", type = "LIVE", url = "erp.client.com" }, Ct);

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("url", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task SimilarNames_WarnsAboutLookAlikes_InTheSameProject()
    {
        var (tester, _, project) = await SeedAsync();
        await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales Return" }, Ct);

        var similar = await (await tester.GetAsync($"/api/lookups/similar?kind=Module&parentId={project.Id}&name=sales%20returns", Ct))
            .ShouldBeOkAsync<List<SimilarName>>();

        similar.Single().Name.ShouldBe("Sales Return");
        similar.Single().Exact.ShouldBeFalse();
    }

    [Fact]
    public async Task ProjectKeySuggestion_SkipsTakenKeys()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Qx Test {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);

        var first = await (await tester.GetAsync($"/api/lookups/project-key?clientId={client.Id}&name=CRM", Ct)).ShouldBeOkAsync<ProjectKeySuggestion>();
        await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "CRM", key = first.Key }, Ct);
        var second = await (await tester.GetAsync($"/api/lookups/project-key?clientId={client.Id}&name=CRM", Ct)).ShouldBeOkAsync<ProjectKeySuggestion>();

        ProjectKeys.IsValid(first.Key).ShouldBeTrue();
        second.Key.ShouldNotBe(first.Key);
        second.Key.ShouldStartWith(first.Key[..Math.Min(first.Key.Length, 7)]);
    }

    [Fact]
    public async Task Members_RejectUnknownUsers()
    {
        var (tester, _, project) = await SeedAsync();

        var response = await tester.PutAsJsonAsync($"/api/projects/{project.Id}/members", new { userIds = new[] { 999999 } }, Ct);

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("userIds", out _).ShouldBeTrue();
    }
}
