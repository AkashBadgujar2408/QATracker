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
using QaTracker.Application.Features.Workflow;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Api.IntegrationTests.Workflow;

public sealed class WorkflowTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(
        HttpClient Tester,
        HttpClient Developer,
        User DeveloperUser,
        ProjectDto Project,
        ModuleDto Module,
        EnvironmentDto Uat,
        EnvironmentDto Live,
        SprintDto Sprint);

    private async Task<Setup> SeedAsync(bool requireLiveVerification = false)
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var developer = (await api.LoginAsync(developerUser)).Client;
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Workflow client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "W" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key, requireLiveVerification }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct))
            .ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var uat = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.example.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var live = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "LIVE", type = "LIVE", url = "https://erp.example.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1 – UAT", phaseId = phases[0].Id, defaultEnvironmentId = uat.Id }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        return new Setup(tester, developer, developerUser, project, module, uat, live, sprint);
    }

    private static async Task<IssueDto> CreateIssueAsync(Setup s, string title = "Quantity accepts negative values", int? assigneeId = null) =>
        await (await s.Tester.PostAsJsonAsync("/api/issues", new
        {
            sprintId = s.Sprint.Id,
            moduleId = s.Module.Id,
            type = "Validation",
            title,
            actualResult = "<p>The order saves.</p>",
            severity = "High",
            priority = "Medium",
            assigneeId,
        }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static async Task CompleteTestingAsync(Setup s)
    {
        var sprint = await (await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body, string? rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        if (rowVersion is not null)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        }

        return client.SendAsync(request, Ct);
    }

    private static async Task<IssueDto> ActAsync(HttpClient client, IssueDto issue, IssueAction action, string? reason = null, RejectType? rejectType = null, string? duplicateOfKey = null) =>
        await (await PostAsync(client, $"/api/issues/{issue.Key}/transition", new { action = action.ToString(), reason, rejectType = rejectType?.ToString(), duplicateOfKey }, issue.RowVersion))
            .ShouldBeOkAsync<IssueDto>();

    private static async Task<IssueDto> FixAsync(HttpClient client, IssueDto issue, params int[] environmentIds) =>
        await (await PostAsync(client, $"/api/issues/{issue.Key}/fixed", new { note = "min=0 on UI and API", environmentIds, version = "1.4.2" }, issue.RowVersion))
            .ShouldBeOkAsync<IssueDto>();

    private async Task<List<Deployment>> DeploymentsAsync(int issueId)
    {
        await using var db = api.CreateDbContext();
        return await db.Deployments.AsNoTracking().Where(d => d.IssueId == issueId).OrderBy(d => d.Id).ToListAsync(Ct);
    }

    private async Task<List<IssueActivity>> ActivitiesAsync(int issueId)
    {
        await using var db = api.CreateDbContext();
        return await db.IssueActivities.AsNoTracking().Where(a => a.IssueId == issueId).OrderBy(a => a.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task Developer_IsBlocked_WhileTheSprintIsInTesting_ButTheTesterCanPreAssign()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s);

        var pick = await s.Developer.PostAsync($"/api/issues/{issue.Key}/pick", null, Ct);
        await pick.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "sprint_in_testing");
        var start = await PostAsync(s.Developer, $"/api/issues/{issue.Key}/transition", new { action = "Start" }, issue.RowVersion);
        await start.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "sprint_in_testing");

        var assigned = await (await PostAsync(s.Tester, $"/api/issues/{issue.Key}/assign", new { userId = s.DeveloperUser.Id, note = "Sales is yours" }, issue.RowVersion))
            .ShouldBeOkAsync<IssueDto>();
        assigned.Status.ShouldBe(IssueStatus.Assigned);
        assigned.AssigneeId.ShouldBe(s.DeveloperUser.Id);

        var activities = await ActivitiesAsync(issue.Id);
        activities.ShouldContain(a => a.Kind == ActivityKind.Assigned && a.NewValue == s.DeveloperUser.Id.ToString() && a.Body == "Sales is yours");
        activities.ShouldContain(a => a.Kind == ActivityKind.StatusChanged && a.OldValue == "New" && a.NewValue == "Assigned");
    }

    [Fact]
    public async Task Pick_IsAtomic_OnlyOneOfManyDevelopersGetsTheIssue()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s);
        await CompleteTestingAsync(s);
        var developers = new List<HttpClient> { s.Developer };
        for (var i = 0; i < 5; i++)
        {
            developers.Add(await api.ClientForAsync(UserRole.Developer));
        }

        var responses = await Task.WhenAll(developers.Select(d => d.PostAsync($"/api/issues/{issue.Key}/pick", null, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        var losers = responses.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();
        losers.Count.ShouldBe(5);
        foreach (var loser in losers)
        {
            loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var problem = await loser.Content.ReadAsStringAsync(Ct);
            problem.ShouldContain("Already picked by");
        }

        var winner = await responses.Single(r => r.StatusCode == HttpStatusCode.OK).ShouldBeOkAsync<IssueDto>();
        winner.Status.ShouldBe(IssueStatus.InProgress);
        (await ActivitiesAsync(issue.Id)).Count(a => a.Kind == ActivityKind.Assigned).ShouldBe(1);
    }

    [Fact]
    public async Task FullLoop_Fix_RetestFails_Reopens_ThenVerifiesAndCloses()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s);
        await CompleteTestingAsync(s);

        issue = await (await s.Developer.PostAsync($"/api/issues/{issue.Key}/pick", null, Ct)).ShouldBeOkAsync<IssueDto>();
        issue = await ActAsync(s.Developer, issue, IssueAction.NeedInfo, "Should zero be allowed?");
        issue.Status.ShouldBe(IssueStatus.NeedInfo);
        issue = await ActAsync(s.Developer, issue, IssueAction.Resume);
        issue = await FixAsync(s.Developer, issue, s.Uat.Id);
        issue.Status.ShouldBe(IssueStatus.Fixed);
        // Until the retest the developer can only reassign or add another deployment (e.g. LIVE).
        issue.AllowedActions.Select(a => a.Action).ShouldBe([IssueAction.Assign, IssueAction.MarkFixed]);

        var uat = (await DeploymentsAsync(issue.Id)).ShouldHaveSingleItem();
        uat.RetestResult.ShouldBe(RetestResult.Pending);
        uat.Version.ShouldBe("1.4.2");

        // Retest failed → Reopened (1).
        (await s.Tester.PostAsJsonAsync($"/api/deployments/{uat.Id}/retest", new { passed = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest); // comment required
        issue = await (await s.Tester.PostAsJsonAsync($"/api/deployments/{uat.Id}/retest", new { passed = false, comment = "Still accepts -5 on paste" }, Ct))
            .ShouldBeOkAsync<IssueDto>();
        issue.Status.ShouldBe(IssueStatus.Reopened);
        issue.ReopenCount.ShouldBe(1);

        // Fixed again, retest passed → Verified, tester closes.
        issue = await ActAsync(s.Developer, issue, IssueAction.Start);
        issue = await FixAsync(s.Developer, issue, s.Uat.Id);
        var second = (await DeploymentsAsync(issue.Id)).Last();
        issue = await (await s.Tester.PostAsJsonAsync($"/api/deployments/{second.Id}/retest", new { passed = true }, Ct)).ShouldBeOkAsync<IssueDto>();
        issue.Status.ShouldBe(IssueStatus.Verified);
        await (await s.Tester.PostAsJsonAsync($"/api/deployments/{second.Id}/retest", new { passed = true }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "already_retested");
        issue = await ActAsync(s.Tester, issue, IssueAction.Close);
        issue.Status.ShouldBe(IssueStatus.Closed);

        // A regression reopens a closed issue and counts again.
        issue = await ActAsync(s.Tester, issue, IssueAction.Reopen, "Back on LIVE after the patch");
        issue.ReopenCount.ShouldBe(2);

        var activities = await ActivitiesAsync(issue.Id);
        activities.Where(a => a.Kind == ActivityKind.StatusChanged).Select(a => a.NewValue).ShouldBe(
        [
            "InProgress", "NeedInfo", "InProgress", "Fixed", "Reopened", "InProgress", "Fixed", "Verified", "Closed", "Reopened",
        ]);
        activities.Count(a => a.Kind == ActivityKind.Deployed).ShouldBe(2);
        activities.ShouldContain(a => a.Kind == ActivityKind.RetestFailed && a.Body == "Still accepts -5 on paste");
        activities.ShouldContain(a => a.Kind == ActivityKind.RetestPassed);

        await using var db = api.CreateDbContext();
        var question = activities.Single(a => a.Kind == ActivityKind.Comment);
        question.Body.ShouldBe("Should zero be allowed?");
        (await db.Mentions.AnyAsync(m => m.ActivityId == question.Id && m.UserId == issue.ReporterId, Ct)).ShouldBeTrue();
        var stored = await db.Issues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, Ct);
        stored.FirstFixedAt.ShouldNotBeNull();
        stored.ClosedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Close_NeedsAPassedLiveRetest_WhenTheProjectRequiresIt()
    {
        var s = await SeedAsync(requireLiveVerification: true);
        var issue = await CreateIssueAsync(s, assigneeId: null);
        await CompleteTestingAsync(s);
        issue = await (await s.Developer.PostAsync($"/api/issues/{issue.Key}/pick", null, Ct)).ShouldBeOkAsync<IssueDto>();
        issue = await FixAsync(s.Developer, issue, s.Uat.Id);
        issue = await ActAsync(s.Tester, issue, IssueAction.Verify);

        var close = await PostAsync(s.Tester, $"/api/issues/{issue.Key}/transition", new { action = "Close" }, issue.RowVersion);
        await close.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "live_verification_required");

        // Verified → Fixed with a LIVE deployment, retest passes, now it closes.
        issue = await FixAsync(s.Developer, issue, s.Live.Id);
        var live = (await DeploymentsAsync(issue.Id)).Single(d => d.EnvironmentId == s.Live.Id);
        issue = await (await s.Tester.PostAsJsonAsync($"/api/deployments/{live.Id}/retest", new { passed = true }, Ct)).ShouldBeOkAsync<IssueDto>();
        issue = await ActAsync(s.Tester, issue, IssueAction.Close);
        issue.Status.ShouldBe(IssueStatus.Closed);
    }

    [Fact]
    public async Task Dispute_ThenReject_AsDuplicate_LinksTheOriginal()
    {
        var s = await SeedAsync();
        var original = await CreateIssueAsync(s, "Negative quantity on sales order");
        var issue = await CreateIssueAsync(s, "Quantity -5 saves", assigneeId: s.DeveloperUser.Id);
        await CompleteTestingAsync(s);
        issue = await (await s.Developer.GetAsync($"/api/issues/{issue.Key}", Ct)).ShouldBeOkAsync<IssueDto>();

        issue = await ActAsync(s.Developer, issue, IssueAction.Dispute, $"Same as {original.Key}");
        var missingKey = await PostAsync(s.Tester, $"/api/issues/{issue.Key}/transition", new { action = "Reject", reason = "Duplicate", rejectType = "Duplicate" }, issue.RowVersion);
        missingKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        issue = await ActAsync(s.Tester, issue, IssueAction.Reject, "Agreed", RejectType.Duplicate, original.Key.ToLowerInvariant());
        issue.Status.ShouldBe(IssueStatus.Rejected);

        await using var db = api.CreateDbContext();
        var stored = await db.Issues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, Ct);
        stored.RejectType.ShouldBe(RejectType.Duplicate);
        stored.DuplicateOfIssueId.ShouldBe(original.Id);
        (await db.IssueLinks.AnyAsync(l => l.FromIssueId == issue.Id && l.ToIssueId == original.Id && l.Type == IssueLinkType.Duplicate, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Transitions_NeedTheCurrentVersion_AndTheRightRole()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s, assigneeId: s.DeveloperUser.Id);
        await CompleteTestingAsync(s);
        issue = await (await s.Developer.GetAsync($"/api/issues/{issue.Key}", Ct)).ShouldBeOkAsync<IssueDto>();

        (await PostAsync(s.Developer, $"/api/issues/{issue.Key}/transition", new { action = "Start" }, null))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        var started = await ActAsync(s.Developer, issue, IssueAction.Start);
        var stale = await PostAsync(s.Developer, $"/api/issues/{issue.Key}/transition", new { action = "Hold", reason = "Waiting for the client" }, issue.RowVersion);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var verifyByDeveloper = await PostAsync(s.Developer, $"/api/issues/{issue.Key}/transition", new { action = "Verify" }, started.RowVersion);
        await verifyByDeveloper.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "role_not_allowed");

        var otherDeveloper = await api.ClientForAsync(UserRole.Developer);
        var fixedByOther = await PostAsync(otherDeveloper, $"/api/issues/{issue.Key}/fixed", new { note = "done", environmentIds = new[] { s.Uat.Id } }, started.RowVersion);
        await fixedByOther.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "not_assignee");

        var noEnvironment = await PostAsync(s.Developer, $"/api/issues/{issue.Key}/fixed", new { note = "done", environmentIds = Array.Empty<int>() }, started.RowVersion);
        noEnvironment.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkAssign_AssignsWhatItCan_AndReportsTheRest()
    {
        var s = await SeedAsync();
        var first = await CreateIssueAsync(s, "First");
        var second = await CreateIssueAsync(s, "Second");
        await CompleteTestingAsync(s);
        var closed = await (await s.Developer.PostAsync($"/api/issues/{second.Key}/pick", null, Ct)).ShouldBeOkAsync<IssueDto>();
        closed = await FixAsync(s.Developer, closed, s.Uat.Id);
        closed = await ActAsync(s.Tester, closed, IssueAction.Verify);
        await ActAsync(s.Tester, closed, IssueAction.Close);
        var rahul = await api.CreateUserAsync(UserRole.Developer);

        var result = await (await s.Developer.PostAsJsonAsync("/api/issues/bulk-assign", new { keys = new[] { first.Key, second.Key, "NOPE-1" }, userId = rahul.Id, note = "BOM is Rahul's" }, Ct))
            .ShouldBeOkAsync<BulkAssignResult>();

        result.Assigned.ShouldBe([first.Key]);
        result.Failed.Select(f => (f.Key, f.Code)).ShouldBe([(second.Key, "invalid_transition"), ("NOPE-1", "not_found")], ignoreOrder: true);
        var reloaded = await (await s.Tester.GetAsync($"/api/issues/{first.Key}", Ct)).ShouldBeOkAsync<IssueDto>();
        reloaded.AssigneeId.ShouldBe(rahul.Id);
        reloaded.Status.ShouldBe(IssueStatus.Assigned);
    }
}
