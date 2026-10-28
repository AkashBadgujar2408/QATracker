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
using QaTracker.Application.Features.WorkQueues;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Workflow;

public sealed class WorkQueuesTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, HttpClient Developer, User DeveloperUser, ProjectDto Project, ModuleDto Module, EnvironmentDto Uat, EnvironmentDto Live, SprintDto Sprint);

    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var developer = (await api.LoginAsync(developerUser)).Client;
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Queue client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "Q" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct))
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

    private static async Task<IssueDto> CreateIssueAsync(Setup s, string title, int? assigneeId = null, string severity = "High") =>
        await (await s.Tester.PostAsJsonAsync("/api/issues", new
        {
            sprintId = s.Sprint.Id,
            moduleId = s.Module.Id,
            type = "Validation",
            title,
            actualResult = "<p>Wrong.</p>",
            severity,
            priority = "Medium",
            assigneeId,
        }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static async Task CompleteTestingAsync(Setup s)
    {
        var sprint = await (await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        return client.SendAsync(request, Ct);
    }

    private static async Task<IssueDto> PickAsync(Setup s, IssueDto issue) =>
        await (await s.Developer.PostAsync($"/api/issues/{issue.Key}/pick", null, Ct)).ShouldBeOkAsync<IssueDto>();

    private static async Task<IssueDto> FixAsync(Setup s, IssueDto issue, params int[] environmentIds) =>
        await (await PostAsync(s.Developer, $"/api/issues/{issue.Key}/fixed", new { note = "Fixed", environmentIds, version = "1.0" }, issue.RowVersion))
            .ShouldBeOkAsync<IssueDto>();

    private static async Task<MyWorkDto> MyWorkAsync(Setup s) =>
        await (await s.Developer.GetAsync($"/api/me/work?projectId={s.Project.Id}", Ct)).ShouldBeOkAsync<MyWorkDto>();

    private static async Task<RetestQueueDto> QueueAsync(HttpClient client, Setup s) =>
        await (await client.GetAsync($"/api/retest-queue?projectId={s.Project.Id}", Ct)).ShouldBeOkAsync<RetestQueueDto>();

    private static Task<HttpResponseMessage> NoticeAsync(HttpClient client, int environmentId, params string[] issueKeys) =>
        client.PostAsJsonAsync("/api/deploy-notices", new { environmentId, issueKeys, note = "Release 1.9", version = "1.9.0" }, Ct);

    private static IEnumerable<string> Keys(WorkQueueDto queue) => queue.Items.Select(i => i.Key);

    [Fact]
    public async Task MyWork_ShowsTheTabs_OnlyOnceTestingIsCompleted()
    {
        var s = await SeedAsync();
        var preAssigned = await CreateIssueAsync(s, "Pre-assigned", s.DeveloperUser.Id);
        var critical = await CreateIssueAsync(s, "Open critical", severity: "Critical");
        var open = await CreateIssueAsync(s, "Open high");

        // While the sprint is in Testing nothing is actionable yet.
        var work = await MyWorkAsync(s);
        work.Assigned.Count.ShouldBe(0);
        work.Available.Count.ShouldBe(0);

        await CompleteTestingAsync(s);
        work = await MyWorkAsync(s);
        Keys(work.Assigned).ShouldBe([preAssigned.Key]);
        Keys(work.Available).ShouldBe([critical.Key, open.Key]); // most severe first
        work.Available.Count.ShouldBe(2);

        // Need info: listed once someone else answers.
        preAssigned = await (await s.Developer.GetAsync($"/api/issues/{preAssigned.Key}", Ct)).ShouldBeOkAsync<IssueDto>(); // due date set by complete testing
        var started = await (await PostAsync(s.Developer, $"/api/issues/{preAssigned.Key}/transition", new { action = "Start" }, preAssigned.RowVersion)).ShouldBeOkAsync<IssueDto>();
        (await PostAsync(s.Developer, $"/api/issues/{preAssigned.Key}/transition", new { action = "NeedInfo", reason = "Zero allowed?" }, started.RowVersion)).EnsureSuccessStatusCode();
        (await MyWorkAsync(s)).NeedInfoAnswered.Count.ShouldBe(0);
        var form = new MultipartFormDataContent { { new StringContent("Zero is allowed."), "body" } };
        (await s.Tester.PostAsync($"/api/issues/{preAssigned.Key}/comments", form, Ct)).EnsureSuccessStatusCode();
        Keys((await MyWorkAsync(s)).NeedInfoAnswered).ShouldBe([preAssigned.Key]);

        // Reopened: a failed retest.
        var fixedIssue = await FixAsync(s, await PickAsync(s, open), s.Uat.Id);
        (await s.Tester.PostAsJsonAsync($"/api/deployments/{fixedIssue.Deployments.Single().Id}/retest", new { passed = false, comment = "Still wrong" }, Ct)).EnsureSuccessStatusCode();

        // Overdue: the due date has passed.
        await using (var db = api.CreateDbContext())
        {
            await db.Issues.Where(i => i.Id == preAssigned.Id).ExecuteUpdateAsync(set => set.SetProperty(i => i.DueDate, DateTimeOffset.UtcNow.AddDays(-1)), Ct);
        }

        work = await MyWorkAsync(s);
        Keys(work.Reopened).ShouldBe([open.Key]);
        Keys(work.Overdue).ShouldBe([preAssigned.Key]);
        Keys(work.Assigned).ShouldBe([preAssigned.Key, open.Key], ignoreOrder: true);
        Keys(work.Available).ShouldBe([critical.Key]);

        // Testers have nothing to pick.
        var testerWork = await (await s.Tester.GetAsync($"/api/me/work?projectId={s.Project.Id}", Ct)).ShouldBeOkAsync<MyWorkDto>();
        testerWork.Available.Count.ShouldBe(0);
    }

    [Fact]
    public async Task RetestQueue_GroupsPendingDeploymentsByEnvironment()
    {
        var s = await SeedAsync();
        var first = await CreateIssueAsync(s, "First");
        var both = await CreateIssueAsync(s, "Both");
        var liveOnly = await CreateIssueAsync(s, "Live only");
        await CompleteTestingAsync(s);
        first = await FixAsync(s, await PickAsync(s, first), s.Uat.Id);
        await FixAsync(s, await PickAsync(s, both), s.Uat.Id, s.Live.Id);
        await FixAsync(s, await PickAsync(s, liveOnly), s.Live.Id);

        var queue = await QueueAsync(s.Tester, s);
        queue.Count.ShouldBe(4);
        queue.Groups.Select(g => g.EnvironmentName).ShouldBe(["UAT 1", "LIVE"]); // oldest waiting first
        queue.Groups[0].EnvironmentUrl.ShouldBe("https://uat.example.com");
        queue.Groups[0].Items.Select(i => i.IssueKey).ShouldBe([first.Key, both.Key]);
        queue.Groups[1].Items.Select(i => i.IssueKey).ShouldBe([both.Key, liveOnly.Key]);
        queue.Groups.SelectMany(g => g.Items).ShouldAllBe(i => i.CanRetest);
        (await QueueAsync(s.Developer, s)).Groups.SelectMany(g => g.Items).ShouldAllBe(i => !i.CanRetest);

        // Passing a retest clears it from the queue.
        (await s.Tester.PostAsJsonAsync($"/api/deployments/{first.Deployments.Single().Id}/retest", new { passed = true }, Ct)).EnsureSuccessStatusCode();
        queue = await QueueAsync(s.Tester, s);
        queue.Count.ShouldBe(3);
        queue.Groups[0].Items.Select(i => i.IssueKey).ShouldBe([both.Key]);

        // Another tester's reported issues are hidden with mine=true.
        var otherTester = await api.ClientForAsync(UserRole.Tester);
        var mine = await (await otherTester.GetAsync($"/api/retest-queue?projectId={s.Project.Id}&mine=true", Ct)).ShouldBeOkAsync<RetestQueueDto>();
        mine.Count.ShouldBe(0);
    }

    [Fact]
    public async Task DeployNotice_AnnouncesOwnFixes_OnOneEnvironment()
    {
        var s = await SeedAsync();
        var fixedOnUat = await CreateIssueAsync(s, "Fixed on UAT");
        var verified = await CreateIssueAsync(s, "Verified on UAT");
        await CompleteTestingAsync(s);
        fixedOnUat = await FixAsync(s, await PickAsync(s, fixedOnUat), s.Uat.Id);
        verified = await FixAsync(s, await PickAsync(s, verified), s.Uat.Id);
        (await s.Tester.PostAsJsonAsync($"/api/deployments/{verified.Deployments.Single().Id}/retest", new { passed = true }, Ct)).EnsureSuccessStatusCode();

        var candidates = await (await s.Developer.GetAsync($"/api/deploy-notices/candidates?projectId={s.Project.Id}", Ct)).ShouldBeOkAsync<List<DeployCandidateDto>>();
        candidates.Select(c => c.Issue.Key).ShouldBe([fixedOnUat.Key, verified.Key]);
        candidates[1].Deployments.Single().RetestResult.ShouldBe(RetestResult.Passed);

        var notice = await (await NoticeAsync(s.Developer, s.Live.Id, fixedOnUat.Key, verified.Key.ToLowerInvariant()))
            .ShouldBeOkAsync<DeployNoticeDto>(HttpStatusCode.Created);
        notice.EnvironmentName.ShouldBe("LIVE");
        notice.Note.ShouldBe("Release 1.9");
        notice.Issues.Select(i => i.IssueKey).ShouldBe([fixedOnUat.Key, verified.Key]);
        notice.Issues.ShouldAllBe(i => i.RetestResult == RetestResult.Pending && i.Version == "1.9.0");

        await using (var db = api.CreateDbContext())
        {
            var reVerified = await db.Issues.AsNoTracking().SingleAsync(i => i.Id == verified.Id, Ct);
            reVerified.Status.ShouldBe(IssueStatus.Fixed);
            reVerified.VerifiedAt.ShouldBeNull();
            foreach (var id in new[] { fixedOnUat.Id, verified.Id })
            {
                var deployed = await db.IssueActivities.AsNoTracking()
                    .Where(a => a.IssueId == id && a.Kind == ActivityKind.Deployed && a.EnvironmentId == s.Live.Id).ToListAsync(Ct);
                deployed.Count.ShouldBe(1); // one timeline entry per issue
                deployed[0].Body.ShouldBe("Release 1.9");
            }
        }

        var queue = await QueueAsync(s.Tester, s);
        queue.Groups.Single(g => g.EnvironmentId == s.Live.Id).Items.Select(i => i.IssueKey).ShouldBe([fixedOnUat.Key, verified.Key]);

        var recent = await (await s.Tester.GetAsync($"/api/deploy-notices?projectId={s.Project.Id}", Ct)).ShouldBeOkAsync<List<DeployNoticeDto>>();
        recent.Single().Id.ShouldBe(notice.Id);
    }

    [Fact]
    public async Task DeployNotice_IsAllOrNothing_AndOnlyForTheCallersFixedIssues()
    {
        var s = await SeedAsync();
        var mine = await CreateIssueAsync(s, "Mine, fixed");
        var inProgress = await CreateIssueAsync(s, "Mine, in progress");
        var others = await CreateIssueAsync(s, "Someone else's");
        await CompleteTestingAsync(s);
        mine = await FixAsync(s, await PickAsync(s, mine), s.Uat.Id);
        await PickAsync(s, inProgress);
        var otherDeveloper = await api.ClientForAsync(UserRole.Developer);
        var otherFixed = await (await otherDeveloper.PostAsync($"/api/issues/{others.Key}/pick", null, Ct)).ShouldBeOkAsync<IssueDto>();
        (await PostAsync(otherDeveloper, $"/api/issues/{others.Key}/fixed", new { note = "Fixed", environmentIds = new[] { s.Uat.Id } }, otherFixed.RowVersion)).EnsureSuccessStatusCode();

        var problem = await (await NoticeAsync(s.Developer, s.Live.Id, mine.Key, inProgress.Key, others.Key, "NOPE-1"))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
        problem.Code().ShouldBe("not_deployable");
        var detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain($"{inProgress.Key}: is not fixed yet");
        detail.ShouldContain($"{others.Key}: is not assigned to you");
        detail.ShouldContain("NOPE-1: not found");
        detail.ShouldNotContain($"{mine.Key}:");

        // Already waiting for a retest on the same environment.
        await (await NoticeAsync(s.Developer, s.Uat.Id, mine.Key)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "not_deployable");

        // A tester cannot announce fixes; validation needs a note and issues.
        await (await NoticeAsync(s.Tester, s.Live.Id, mine.Key)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "not_deployable");
        (await s.Developer.PostAsJsonAsync("/api/deploy-notices", new { environmentId = s.Live.Id, issueKeys = Array.Empty<string>() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing was written by the rejected notices.
        await using var db = api.CreateDbContext();
        (await db.Deployments.CountAsync(d => d.IssueId == mine.Id, Ct)).ShouldBe(1);
        (await db.DeployNotices.CountAsync(n => n.ProjectId == s.Project.Id, Ct)).ShouldBe(0);
    }
}
