using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Environments;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Notifications;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Notifications;

public sealed class NotificationsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(
        HttpClient Tester,
        User TesterUser,
        string TesterToken,
        HttpClient Developer,
        User DeveloperUser,
        string DeveloperToken,
        ProjectDto Project,
        ModuleDto Module,
        EnvironmentDto Uat,
        SprintDto Sprint);

    private async Task<Setup> SeedAsync()
    {
        var testerUser = await api.CreateUserAsync(UserRole.Tester);
        var (tester, testerSession) = await api.LoginAsync(testerUser);
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var (developer, developerSession) = await api.LoginAsync(developerUser);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Notify client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "N" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct))
            .ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var uat = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.example.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1", phaseId = phases[0].Id, defaultEnvironmentId = uat.Id }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        return new Setup(tester, testerUser, testerSession.AccessToken, developer, developerUser, developerSession.AccessToken, project, module, uat, sprint);
    }

    private static async Task<IssueDto> CreateIssueAsync(Setup s, string title, int? assigneeId = null) =>
        await (await s.Tester.PostAsJsonAsync("/api/issues", new
        {
            sprintId = s.Sprint.Id,
            moduleId = s.Module.Id,
            type = "Validation",
            title,
            actualResult = "<p>Wrong total.</p>",
            severity = "High",
            priority = "High",
            assigneeId,
        }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static async Task CompleteTestingAsync(Setup s)
    {
        var sprint = await (await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task<IssueDto> PostAsync(HttpClient client, string url, object body, string rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        return await (await client.SendAsync(request, Ct)).ShouldBeOkAsync<IssueDto>();
    }

    private static async Task<IssueDto> ReloadAsync(HttpClient client, IssueDto issue) =>
        await (await client.GetAsync($"/api/issues/{issue.Key}", Ct)).ShouldBeOkAsync<IssueDto>();

    private static Task<IssueDto> ActAsync(HttpClient client, IssueDto issue, string action, string? reason = null) =>
        PostAsync(client, $"/api/issues/{issue.Key}/transition", new { action, reason }, issue.RowVersion);

    private static Task<IssueDto> FixAsync(Setup s, IssueDto issue) =>
        PostAsync(s.Developer, $"/api/issues/{issue.Key}/fixed", new { note = "Totals rounded", environmentIds = new[] { s.Uat.Id }, version = "1.2" }, issue.RowVersion);

    private static async Task CommentAsync(HttpClient client, string key, string body)
    {
        var form = new MultipartFormDataContent { { new StringContent(body), "body" } };
        (await client.PostAsync($"/api/issues/{key}/comments", form, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<NotificationPage> NotificationsAsync(HttpClient client, string query = "") =>
        await (await client.GetAsync($"/api/notifications{query}", Ct)).ShouldBeOkAsync<NotificationPage>();

    private async Task<HubConnection> ConnectAsync(string accessToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost/hubs/tracker"), options =>
            {
                options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling; // the test server has no WebSockets
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        await connection.StartAsync(Ct);
        return connection;
    }

    private static async Task<T> WithinAsync<T>(Task<T> task) =>
        await task.WaitAsync(TimeSpan.FromSeconds(15), Ct);

    [Fact]
    public async Task WorkflowSteps_NotifyTheRightPerson_Once_AndNeverTheActor()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s, "Invoice total is wrong", s.DeveloperUser.Id);

        // Pre-assignment during Testing: nothing yet – the developer hears it when testing completes.
        (await NotificationsAsync(s.Developer)).Items.ShouldBeEmpty();
        await CompleteTestingAsync(s);
        var developerInbox = await NotificationsAsync(s.Developer);
        developerInbox.Items.ShouldHaveSingleItem().Type.ShouldBe(NotificationType.TestingCompleted);
        developerInbox.Items[0].ProjectId.ShouldBe(s.Project.Id);

        issue = await ActAsync(s.Developer, await ReloadAsync(s.Developer, issue), "Start");
        issue = await ActAsync(s.Developer, issue, "NeedInfo", "Which customer shows the wrong total?");

        // The question mentions the reporter, but the reporter gets one Need info notification, not three.
        var testerInbox = await NotificationsAsync(s.Tester);
        var needInfo = testerInbox.Items.ShouldHaveSingleItem();
        needInfo.Type.ShouldBe(NotificationType.NeedInfo);
        needInfo.IssueKey.ShouldBe(issue.Key);
        needInfo.Body!.ShouldContain("Which customer");
        testerInbox.UnreadCount.ShouldBe(1);

        await CommentAsync(s.Tester, issue.Key, $"@{s.DeveloperUser.UserName} customer ACME, order 1042.");
        developerInbox = await NotificationsAsync(s.Developer);
        developerInbox.Items[0].Type.ShouldBe(NotificationType.Mentioned);
        developerInbox.Items.Count(n => n.IssueKey == issue.Key).ShouldBe(1); // mentioned, not also "commented"

        issue = await (await s.Developer.GetAsync($"/api/issues/{issue.Key}", Ct)).ShouldBeOkAsync<IssueDto>();
        issue = await ActAsync(s.Developer, issue, "Resume");
        issue = await FixAsync(s, issue);
        (await NotificationsAsync(s.Tester)).Items[0].ShouldSatisfyAllConditions(
            n => n.Type.ShouldBe(NotificationType.Fixed),
            n => n.Body!.ShouldContain("Totals rounded"));

        await ActAsync(s.Tester, issue, "Reopen", "Still wrong with discounts");
        developerInbox = await NotificationsAsync(s.Developer);
        developerInbox.Items[0].Type.ShouldBe(NotificationType.Reopened);
        developerInbox.Items[0].Title.ShouldContain(issue.Key);

        // The actor never hears about their own steps.
        (await NotificationsAsync(s.Developer)).Items.ShouldNotContain(n => n.Type == NotificationType.NeedInfo || n.Type == NotificationType.Fixed);
        (await NotificationsAsync(s.Tester)).Items.ShouldNotContain(n => n.Type == NotificationType.Reopened || n.Type == NotificationType.Mentioned);
    }

    [Fact]
    public async Task DeployNotice_GivesTheReporterOneNotificationPerBatch()
    {
        var s = await SeedAsync();
        var first = await CreateIssueAsync(s, "Tax rounding", s.DeveloperUser.Id);
        var second = await CreateIssueAsync(s, "Discount rounding", s.DeveloperUser.Id);
        await CompleteTestingAsync(s);
        var live = await (await s.Tester.PostAsJsonAsync($"/api/projects/{s.Project.Id}/environments", new { name = "LIVE", type = "LIVE", url = "https://erp.example.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        foreach (var issue in new[] { first, second })
        {
            var started = await ActAsync(s.Developer, await ReloadAsync(s.Developer, issue), "Start");
            await FixAsync(s, started);
        }

        var before = (await NotificationsAsync(s.Tester)).Items.Count;
        (await s.Developer.PostAsJsonAsync("/api/deploy-notices", new { environmentId = live.Id, issueKeys = new[] { first.Key, second.Key }, note = "Release 2.0", version = "2.0.0" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var inbox = await NotificationsAsync(s.Tester);
        inbox.Items.Count.ShouldBe(before + 1);
        inbox.Items[0].ShouldSatisfyAllConditions(
            n => n.Type.ShouldBe(NotificationType.Fixed),
            n => n.Title.ShouldBe("2 fixes deployed on LIVE – ready to retest"),
            n => n.IssueKey.ShouldBeNull(),
            n => n.ProjectId.ShouldBe(s.Project.Id),
            n => n.Body!.ShouldContain("version 2.0.0"),
            n => n.Body!.ShouldContain(first.Key));
    }

    [Fact]
    public async Task History_KeepsReadAndUnread_PagesBack_AndMarksOnlyOwnNotifications()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s, "History check", s.DeveloperUser.Id);
        await CompleteTestingAsync(s);
        for (var i = 1; i <= 4; i++)
        {
            await CommentAsync(s.Tester, issue.Key, $"Note {i}");
        }

        // Each comment is its own notification: TestingCompleted + 4 comments.
        var all = await NotificationsAsync(s.Developer);
        all.Items.Count.ShouldBe(5);
        all.UnreadCount.ShouldBe(5);

        var firstPage = await NotificationsAsync(s.Developer, "?size=2");
        firstPage.Items.Count.ShouldBe(2);
        firstPage.HasMore.ShouldBeTrue();
        firstPage.Items[0].Body!.ShouldContain("Note 4");
        var older = await NotificationsAsync(s.Developer, $"?size=2&before={firstPage.Items[^1].Id}");
        older.Items[0].Body!.ShouldContain("Note 2");

        // Someone else's ids are ignored.
        var stranger = await api.ClientForAsync(UserRole.Developer);
        (await (await stranger.PostAsJsonAsync("/api/notifications/read", new { ids = new[] { all.Items[0].Id } }, Ct)).ShouldBeOkAsync<UnreadCountDto>()).UnreadCount.ShouldBe(0);
        (await NotificationsAsync(s.Developer)).UnreadCount.ShouldBe(5);

        var afterOne = await (await s.Developer.PostAsJsonAsync("/api/notifications/read", new { ids = new[] { all.Items[0].Id } }, Ct)).ShouldBeOkAsync<UnreadCountDto>();
        afterOne.UnreadCount.ShouldBe(4);
        (await NotificationsAsync(s.Developer, "?unread=true")).Items.ShouldNotContain(n => n.Id == all.Items[0].Id);

        (await s.Developer.PostAsJsonAsync("/api/notifications/read", new { ids = new[] { all.Items[0].Id }, read = false }, Ct)).EnsureSuccessStatusCode();
        (await (await s.Developer.GetAsync("/api/notifications/unread-count", Ct)).ShouldBeOkAsync<UnreadCountDto>()).UnreadCount.ShouldBe(5);

        (await (await s.Developer.PostAsJsonAsync("/api/notifications/read", new { all = true }, Ct)).ShouldBeOkAsync<UnreadCountDto>()).UnreadCount.ShouldBe(0);
        var history = await NotificationsAsync(s.Developer);
        history.Items.Count.ShouldBe(5); // read ones stay in the history
        history.Items.ShouldAllBe(n => n.IsRead);

        (await s.Developer.PostAsJsonAsync("/api/notifications/read", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Developer.GetAsync("/api/notifications?size=500", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Hub_PushesCommentsAndNotifications_ToOtherBrowsers_AfterCommit()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s, "Live check", s.DeveloperUser.Id);
        await CompleteTestingAsync(s);

        await using var developerBrowser = await ConnectAsync(s.DeveloperToken);
        await using var testerBrowser = await ConnectAsync(s.TesterToken);
        var notifications = new ConcurrentQueue<NotificationDto>();
        var notificationArrived = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        developerBrowser.On<NotificationDto>("NotificationCreated", n =>
        {
            notifications.Enqueue(n);
            notificationArrived.TrySetResult(n);
        });
        var activityArrived = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        developerBrowser.On<JsonElement>("ActivityAdded", a => activityArrived.TrySetResult(a));
        var issueChanged = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        testerBrowser.On<JsonElement>("IssueChanged", e =>
        {
            if (e.GetProperty("issueKey").GetString() == issue.Key)
            {
                issueChanged.TrySetResult(e); // other tests run in parallel: every signed-in user gets every issue change
            }
        });
        var readElsewhere = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        developerBrowser.On<JsonElement>("NotificationsRead", e => readElsewhere.TrySetResult(e));
        await developerBrowser.InvokeAsync("WatchIssue", issue.Key.ToLowerInvariant(), Ct);

        await CommentAsync(s.Tester, issue.Key, "Please check order 7.");

        var activity = await WithinAsync(activityArrived.Task);
        activity.GetProperty("issueKey").GetString().ShouldBe(issue.Key);
        activity.GetProperty("kind").GetString().ShouldBe("Comment");
        var notification = await WithinAsync(notificationArrived.Task);
        notification.Type.ShouldBe(NotificationType.Comment);
        notification.IssueKey.ShouldBe(issue.Key);
        notification.Id.ShouldBeGreaterThan(0); // sent after the commit, with the stored id
        (await WithinAsync(issueChanged.Task)).GetProperty("issueKey").GetString().ShouldBe(issue.Key);

        // Reading in one tab updates the bell of the user's other tabs.
        await s.Developer.PostAsJsonAsync("/api/notifications/read", new { all = true }, Ct);
        (await WithinAsync(readElsewhere.Task)).GetProperty("unreadCount").GetInt32().ShouldBe(0);
        notifications.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Hub_RejectsAnonymousConnections()
    {
        await api.ClientForAsync(UserRole.Tester); // starts the server
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost/hubs/tracker"), options =>
            {
                options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await using (connection)
        {
            await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(Ct));
        }
    }
}
