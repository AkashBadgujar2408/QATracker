using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Environments;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Issues;

public sealed class IssuesTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, ProjectDto Project, ModuleDto Module, EnvironmentDto Environment, SprintDto Sprint);

    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Issue client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "I" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct))
            .ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var environment = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.example.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1 – UAT", phaseId = phases[0].Id, defaultEnvironmentId = environment.Id }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        return new Setup(tester, project, module, environment, sprint);
    }

    private static object NewIssue(Setup s, string title = "Quantity accepts negative values", string severity = "High", int? assigneeId = null) => new
    {
        sprintId = s.Sprint.Id,
        moduleId = s.Module.Id,
        screenName = "Sales Order",
        type = "Validation",
        title,
        steps = "<ol><li>Open a sales order</li><li>Enter -5</li></ol>",
        actualResult = "<p>The order saves.</p>",
        expectedResult = "<p>A validation message.</p>",
        severity,
        priority = "Medium",
        assigneeId,
    };

    private static async Task<IssueDto> CreateAsync(HttpClient client, object body) =>
        await (await client.PostAsJsonAsync("/api/issues", body, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static object UpdateBody(IssueDto issue, string? title = null, string? severity = null, int? assigneeId = null) => new
    {
        moduleId = issue.ModuleId,
        screenName = issue.ScreenName,
        foundOnEnvironmentId = issue.FoundOnEnvironmentId,
        type = issue.Type.ToString(),
        title = title ?? issue.Title,
        steps = issue.Steps,
        expectedResult = issue.ExpectedResult,
        actualResult = issue.ActualResult,
        severity = severity ?? issue.Severity.ToString(),
        priority = issue.Priority.ToString(),
        assigneeId = assigneeId ?? issue.AssigneeId,
    };

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, IssueDto issue, object body, string? rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/issues/{issue.Key}") { Content = JsonContent.Create(body) };
        if (rowVersion is not null)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        }

        return client.SendAsync(request, Ct);
    }

    private static async Task CompleteTestingAsync(Setup s)
    {
        var sprint = await (await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Create_NumbersTheIssue_DefaultsTheEnvironment_AndWritesActivities()
    {
        var s = await SeedAsync();
        var developer = await api.CreateUserAsync(UserRole.Developer);

        var response = await s.Tester.PostAsJsonAsync("/api/issues", NewIssue(s, assigneeId: developer.Id), Ct);
        var issue = await response.ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

        issue.Key.ShouldBe($"{s.Project.Key}-1");
        response.Headers.Location!.ToString().ShouldBe($"/api/issues/{issue.Key}");
        response.Headers.ETag!.Tag.ShouldBe($"\"{issue.RowVersion}\"");
        issue.Status.ShouldBe(IssueStatus.Assigned);
        issue.FoundOnEnvironmentId.ShouldBe(s.Environment.Id);
        issue.Permissions.ShouldBe(new IssuePermissions(true, true));

        await using var db = api.CreateDbContext();
        var kinds = await db.IssueActivities.Where(a => a.IssueId == issue.Id).Select(a => a.Kind).ToListAsync(Ct);
        kinds.ShouldBe([ActivityKind.Created, ActivityKind.Assigned], ignoreOrder: true);
    }

    [Fact]
    public async Task ConcurrentCreates_GetUniqueConsecutiveNumbers()
    {
        var s = await SeedAsync();
        var second = (await api.LoginAsync(await api.CreateUserAsync(UserRole.Tester))).Client;

        var responses = await Task.WhenAll(Enumerable.Range(1, 20)
            .Select(n => (n % 2 == 0 ? s.Tester : second).PostAsJsonAsync("/api/issues", NewIssue(s, $"Parallel issue {n}"), Ct)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var issues = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<IssueDto>(ApiFactory.Json, Ct)));
        issues.Select(i => i!.Number).Order().ShouldBe(Enumerable.Range(1, 20));
    }

    [Fact]
    public async Task Create_RequiresSeverityAndRealActualResult_AndSanitisesHtml()
    {
        var s = await SeedAsync();

        var noSeverity = await s.Tester.PostAsJsonAsync("/api/issues", new { sprintId = s.Sprint.Id, moduleId = s.Module.Id, type = "Bug", title = "X", actualResult = "Y", priority = "Low" }, Ct);
        (await noSeverity.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("severity", out _).ShouldBeTrue();

        var emptyActual = await s.Tester.PostAsJsonAsync("/api/issues", new { sprintId = s.Sprint.Id, moduleId = s.Module.Id, type = "Bug", title = "X", actualResult = "<p> </p>", severity = "Low", priority = "Low" }, Ct);
        (await emptyActual.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("actualResult", out _).ShouldBeTrue();

        var issue = await CreateAsync(s.Tester, new
        {
            sprintId = s.Sprint.Id,
            moduleId = s.Module.Id,
            type = "Security",
            title = "Script in steps",
            steps = "<p onclick=\"steal()\">Click <a href=\"javascript:alert(1)\">here</a></p><script>alert(1)</script><img src=x onerror=alert(1)>",
            actualResult = "<p>Runs <strong>script</strong></p>",
            severity = "NotAssessed",
            priority = "High",
        });

        issue.Steps.ShouldNotBeNull();
        issue.Steps.ShouldNotContain("script", Case.Insensitive);
        issue.Steps.ShouldNotContain("onclick");
        issue.Steps.ShouldNotContain("javascript:");
        issue.Steps.ShouldNotContain("<img");
        issue.ActualResult.ShouldBe("<p>Runs <strong>script</strong></p>");
        issue.Severity.ShouldBe(Severity.NotAssessed);
    }

    [Fact]
    public async Task Update_NeedsIfMatch_Rejects_StaleVersions_AndLogsChanges()
    {
        var s = await SeedAsync();
        var issue = await CreateAsync(s.Tester, NewIssue(s));

        (await PutAsync(s.Tester, issue, UpdateBody(issue, title: "New title"), rowVersion: null)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        var updated = await (await PutAsync(s.Tester, issue, UpdateBody(issue, title: "Negative quantity saves", severity: "Critical"), issue.RowVersion))
            .ShouldBeOkAsync<IssueDto>();
        updated.Title.ShouldBe("Negative quantity saves");
        updated.Severity.ShouldBe(Severity.Critical);

        var stale = await PutAsync(s.Tester, issue, UpdateBody(issue, title: "Someone else's edit"), issue.RowVersion);
        (await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("concurrency");

        await using var db = api.CreateDbContext();
        var changes = await db.IssueActivities.Where(a => a.IssueId == issue.Id && a.Kind == ActivityKind.FieldChanged)
            .Select(a => new { a.Body, a.OldValue, a.NewValue }).ToListAsync(Ct);
        changes.ShouldContain(c => c.Body == "Title" && c.OldValue == issue.Title && c.NewValue == "Negative quantity saves");
        changes.ShouldContain(c => c.Body == "Severity" && c.OldValue == "High" && c.NewValue == "Critical");
        changes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AfterTestingCompleted_TesterCannotEditCreateOrDelete_AndDeveloperNeverCan()
    {
        var s = await SeedAsync();
        var issue = await CreateAsync(s.Tester, NewIssue(s));
        var developer = await api.ClientForAsync(UserRole.Developer);

        (await PutAsync(developer, issue, UpdateBody(issue, title: "Dev edit"), issue.RowVersion)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await CompleteTestingAsync(s);
        var current = await (await s.Tester.GetAsync($"/api/issues/{issue.Key.ToLowerInvariant()}", Ct)).ShouldBeOkAsync<IssueDto>();
        current.SprintStatus.ShouldBe(SprintStatus.TestingCompleted);
        current.Permissions.ShouldBe(IssuePermissions.None);
        current.DueDate.ShouldNotBeNull();

        (await (await PutAsync(s.Tester, current, UpdateBody(current, title: "Late edit"), current.RowVersion))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("sprint_not_testing");
        (await (await s.Tester.PostAsJsonAsync("/api/issues", NewIssue(s), Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("sprint_not_testing");
        (await (await s.Tester.DeleteAsync($"/api/issues/{issue.Key}", Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("sprint_not_testing");
    }

    [Fact]
    public async Task Delete_RemovesTheIssueAndInboundLinks()
    {
        var s = await SeedAsync();
        var target = await CreateAsync(s.Tester, NewIssue(s, "Target"));
        var other = await CreateAsync(s.Tester, NewIssue(s, "Links to target"));
        await using (var db = api.CreateDbContext())
        {
            db.IssueLinks.Add(new IssueLink { FromIssueId = other.Id, ToIssueId = target.Id, Type = IssueLinkType.Related, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        (await s.Tester.DeleteAsync($"/api/issues/{target.Key}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Tester.GetAsync($"/api/issues/{target.Key}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using var check = api.CreateDbContext();
        (await check.IssueLinks.AnyAsync(l => l.ToIssueId == target.Id, Ct)).ShouldBeFalse();
        (await check.IssueActivities.AnyAsync(a => a.IssueId == target.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task List_Filters_Sorts_AndPages()
    {
        var s = await SeedAsync();
        var developer = await api.CreateUserAsync(UserRole.Developer);
        await CreateAsync(s.Tester, NewIssue(s, "Low thing", "Low"));
        var critical = await CreateAsync(s.Tester, NewIssue(s, "Crash on save", "Critical", developer.Id));
        await CreateAsync(s.Tester, NewIssue(s, "Medium thing", "Medium"));
        var reader = await api.ClientForAsync(UserRole.Developer);

        var bySeverity = await (await reader.GetAsync($"/api/issues?projectId={s.Project.Id}&sort=severity", Ct)).ShouldBeOkAsync<PagedResult<IssueListItemDto>>();
        bySeverity.Items.Select(i => i.Severity).ShouldBe([Severity.Critical, Severity.Medium, Severity.Low]);

        var filtered = await (await reader.GetAsync($"/api/issues?projectId={s.Project.Id}&severity=Critical&severity=Medium&q=crash", Ct)).ShouldBeOkAsync<PagedResult<IssueListItemDto>>();
        filtered.Items.Single().Key.ShouldBe(critical.Key);

        var unassigned = await (await reader.GetAsync($"/api/issues?sprintId={s.Sprint.Id}&unassigned=true", Ct)).ShouldBeOkAsync<PagedResult<IssueListItemDto>>();
        unassigned.TotalCount.ShouldBe(2);

        var mine = await (await reader.GetAsync($"/api/issues?projectId={s.Project.Id}&assigneeId={developer.Id}", Ct)).ShouldBeOkAsync<PagedResult<IssueListItemDto>>();
        mine.Items.Single().AssigneeName.ShouldBe(developer.FullName);

        var page = await (await reader.GetAsync($"/api/issues?projectId={s.Project.Id}&size=2&page=2", Ct)).ShouldBeOkAsync<PagedResult<IssueListItemDto>>();
        (page.TotalCount, page.Items.Count, page.Items.Single().Title).ShouldBe((3, 1, "Low thing"));

        await using (var db = api.CreateDbContext())
        {
            var issue = await db.Issues.SingleAsync(i => i.Id == critical.Id, Ct);
            issue.DueDate = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync(Ct);
        }

        var overdue = await (await reader.GetAsync($"/api/issues?projectId={s.Project.Id}&overdue=true", Ct)).ShouldBeOkAsync<PagedResult<IssueListItemDto>>();
        overdue.Items.Single().Id.ShouldBe(critical.Id);

        (await (await reader.GetAsync("/api/issues?sort=reporter", Ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("sort", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task ScreenSuggestions_PreferTheModuleAndTheMostUsed()
    {
        var s = await SeedAsync();
        var otherModule = await (await s.Tester.PostAsJsonAsync($"/api/projects/{s.Project.Id}/modules", new { name = "Inventory" }, Ct))
            .ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        await CreateAsync(s.Tester, NewIssue(s));
        await CreateAsync(s.Tester, NewIssue(s));
        await CreateAsync(s.Tester, new { sprintId = s.Sprint.Id, moduleId = otherModule.Id, screenName = "Stock Transfer", type = "Bug", title = "T", actualResult = "A", severity = "Low", priority = "Low" });

        var forInventory = await (await s.Tester.GetAsync($"/api/projects/{s.Project.Id}/screens?moduleId={otherModule.Id}", Ct)).ShouldBeOkAsync<List<string>>();
        forInventory.ShouldBe(["Stock Transfer", "Sales Order"]);

        var search = await (await s.Tester.GetAsync($"/api/projects/{s.Project.Id}/screens?q=order", Ct)).ShouldBeOkAsync<List<string>>();
        search.ShouldBe(["Sales Order"]);
    }

    [Fact]
    public async Task Get_ReturnsAllowedActions_FromIssueWorkflow()
    {
        var s = await SeedAsync();
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var developer = (await api.LoginAsync(developerUser)).Client;
        var issue = await CreateAsync(s.Tester, NewIssue(s));

        // In testing: the tester may only (pre-)assign; developers may do nothing yet.
        var assign = issue.AllowedActions.ShouldHaveSingleItem();
        assign.Action.ShouldBe(Domain.Rules.IssueAction.Assign);
        assign.Target.ShouldBe(IssueStatus.Assigned);
        assign.Requires.ShouldBe(["assignee"]);
        (await (await developer.GetAsync($"/api/issues/{issue.Key}", Ct)).ShouldBeOkAsync<IssueDto>()).AllowedActions.ShouldBeEmpty();

        await CompleteTestingAsync(s);

        var forDeveloper = await (await developer.GetAsync($"/api/issues/{issue.Key}", Ct)).ShouldBeOkAsync<IssueDto>();
        forDeveloper.AllowedActions.Select(a => a.Action).ShouldBe([Domain.Rules.IssueAction.Assign, Domain.Rules.IssueAction.Pick]);
        forDeveloper.AllowedActions.Single(a => a.Action == Domain.Rules.IssueAction.Pick).Target.ShouldBe(IssueStatus.InProgress);
    }
}
