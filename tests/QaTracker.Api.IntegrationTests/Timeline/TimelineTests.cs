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
using QaTracker.Application.Features.Timeline;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Timeline;

public sealed class TimelineTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, User TesterUser, HttpClient Developer, User DeveloperUser, ModuleDto Module, SprintDto Sprint);

    private async Task<Setup> SeedAsync()
    {
        var testerUser = await api.CreateUserAsync(UserRole.Tester);
        var tester = (await api.LoginAsync(testerUser)).Client;
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var developer = (await api.LoginAsync(developerUser)).Client;
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Timeline client {Guid.NewGuid():N}" }, Ct)).ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "T" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct)).ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.example.com" }, Ct)).ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1 – UAT", phaseId = phases[0].Id }, Ct)).ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        return new Setup(tester, testerUser, developer, developerUser, module, sprint);
    }

    private static async Task<IssueDto> CreateIssueAsync(Setup s, string title = "Quantity accepts negative values", string? screen = "Sales Order", int? assigneeId = null) =>
        await (await s.Tester.PostAsJsonAsync("/api/issues", new
        {
            sprintId = s.Sprint.Id,
            moduleId = s.Module.Id,
            screenName = screen,
            type = "Bug",
            title,
            actualResult = "<p>It saves.</p>",
            severity = "High",
            priority = "High",
            assigneeId,
        }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static Task<HttpResponseMessage> CommentAsync(HttpClient client, string key, string? body, params (string Name, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        if (body is not null)
        {
            form.Add(new StringContent(body), "body");
        }

        foreach (var (name, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(part, "files", name);
        }

        return client.PostAsync($"/api/issues/{key}/comments", form, Ct);
    }

    private static async Task<TimelinePage> TimelineAsync(HttpClient client, string key, string query = "") =>
        await (await client.GetAsync($"/api/issues/{key}/activities{query}", Ct)).ShouldBeOkAsync<TimelinePage>();

    [Fact]
    public async Task Timeline_ReturnsCommentsAndEvents_InOrder_WithUserNames()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s, assigneeId: s.DeveloperUser.Id);

        var question = await (await CommentAsync(s.Developer, issue.Key, $"Should zero be allowed? @{s.TesterUser.UserName}"))
            .ShouldBeOkAsync<ActivityDto>(HttpStatusCode.Created);
        await (await CommentAsync(s.Tester, issue.Key, "Zero is allowed.", ("steps.txt", "1. open\n2. type 0"u8.ToArray())))
            .ShouldBeOkAsync<ActivityDto>(HttpStatusCode.Created);

        var page = await TimelineAsync(s.Tester, issue.Key);

        page.HasMore.ShouldBeFalse();
        page.Items.Select(a => a.Kind).ShouldBe([ActivityKind.Created, ActivityKind.Assigned, ActivityKind.Comment, ActivityKind.Comment]);
        page.Items[0].Summary.ShouldBe($"{s.TesterUser.FullName} reported the issue");
        page.Items[1].Summary.ShouldBe($"{s.TesterUser.FullName} assigned to {s.DeveloperUser.FullName}");
        page.Items[2].UserName.ShouldBe(s.DeveloperUser.FullName);
        page.Items[2].Mentions.ShouldHaveSingleItem().UserId.ShouldBe(s.TesterUser.Id);
        page.Items[2].CanEdit.ShouldBeFalse(); // the developer's comment, read by the tester
        page.Items[3].CanEdit.ShouldBeTrue();
        var file = page.Items[3].Attachments.ShouldHaveSingleItem();
        file.FileName.ShouldBe("steps.txt");
        (await s.Tester.GetAsync(new Uri(file.Url, UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        question.Mentions.ShouldHaveSingleItem().UserName.ShouldBe(s.TesterUser.UserName);

        var comments = await TimelineAsync(s.Tester, issue.Key, "?filter=Comments");
        comments.Items.ShouldAllBe(a => a.Kind == ActivityKind.Comment);
        var history = await TimelineAsync(s.Tester, issue.Key, "?filter=History");
        history.Items.ShouldAllBe(a => a.Kind != ActivityKind.Comment);
    }

    [Fact]
    public async Task Timeline_PagesBackwards_NewestPageFirst()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s);
        for (var i = 1; i <= 5; i++)
        {
            (await CommentAsync(s.Tester, issue.Key, $"Note {i}")).EnsureSuccessStatusCode();
        }

        var newest = await TimelineAsync(s.Tester, issue.Key, "?size=3");
        newest.HasMore.ShouldBeTrue();
        newest.Items.Select(a => a.Body).ShouldBe(["Note 3", "Note 4", "Note 5"]);

        var older = await TimelineAsync(s.Tester, issue.Key, $"?size=3&before={newest.Items[0].Id}");
        older.HasMore.ShouldBeFalse();
        older.Items.Select(a => a.Body ?? a.Kind.ToString()).ShouldBe(["Created", "Note 1", "Note 2"]);
    }

    [Fact]
    public async Task Comments_WorkForDevelopers_WhileTestingIsInProgress_AndAddWatchers()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s);

        (await CommentAsync(s.Developer, issue.Key, "I'll take this once testing is done")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CommentAsync(s.Developer, issue.Key, "  ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = api.CreateDbContext();
        var watchers = await db.IssueWatchers.Where(w => w.IssueId == issue.Id).Select(w => w.UserId).ToListAsync(Ct);
        watchers.ShouldBe([s.TesterUser.Id, s.DeveloperUser.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task EditComment_OnlyByTheAuthor_WithinTenMinutes_UpdatesMentions()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s);
        var comment = await (await CommentAsync(s.Tester, issue.Key, $"Over to @{s.DeveloperUser.UserName}")).ShouldBeOkAsync<ActivityDto>(HttpStatusCode.Created);

        (await s.Developer.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "hijack" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var edited = await (await s.Tester.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "Over to the team" }, Ct)).ShouldBeOkAsync<ActivityDto>();
        edited.Body.ShouldBe("Over to the team");
        edited.EditedAt.ShouldNotBeNull();
        edited.Mentions.ShouldBeEmpty();

        await using (var db = api.CreateDbContext())
        {
            await db.IssueActivities.Where(a => a.Id == comment.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.CreatedAt, a => a.CreatedAt.AddMinutes(-11)), Ct);
        }

        var late = await s.Tester.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "too late" }, Ct);
        (await late.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("edit_window_passed");

        // System events are not comments.
        var created = (await TimelineAsync(s.Tester, issue.Key)).Items.First();
        (await s.Tester.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "rewrite history" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Related_ListsOpenIssuesOfTheModule_SameScreenFirst()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s, "Negative quantity");
        var otherScreen = await CreateIssueAsync(s, "Grid header", screen: "Sales Return");
        var sameScreen = await CreateIssueAsync(s, "Rate rounds wrongly");

        var related = await (await s.Developer.GetAsync($"/api/issues/{issue.Key}/related", Ct)).ShouldBeOkAsync<List<RelatedIssueDto>>();

        related.Select(r => r.Key).ShouldBe([sameScreen.Key, otherScreen.Key]);
        related[0].SameScreen.ShouldBeTrue();
        related[1].SameScreen.ShouldBeFalse();
    }
}
