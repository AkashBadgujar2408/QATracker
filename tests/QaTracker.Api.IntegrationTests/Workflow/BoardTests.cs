using System.Net;
using System.Net.Http.Json;
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
using QaTracker.Domain.Rules;

namespace QaTracker.Api.IntegrationTests.Workflow;

public sealed class BoardTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, HttpClient Developer, User DeveloperUser, ProjectDto Project, ModuleDto Module, SprintDto Sprint);

    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var developer = (await api.LoginAsync(developerUser)).Client;
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Board client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "B" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct))
            .ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var uat = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.example.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1 – UAT", phaseId = phases[0].Id, defaultEnvironmentId = uat.Id }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        return new Setup(tester, developer, developerUser, project, module, sprint);
    }

    private static async Task<IssueDto> CreateIssueAsync(Setup s, string title, string severity = "High") =>
        await (await s.Tester.PostAsJsonAsync("/api/issues", new
        {
            sprintId = s.Sprint.Id,
            moduleId = s.Module.Id,
            type = "Bug",
            title,
            actualResult = "<p>Wrong.</p>",
            severity,
            priority = "Medium",
        }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static async Task<BoardDto> BoardAsync(HttpClient client, Setup s, string extra = "") =>
        await (await client.GetAsync($"/api/board?projectId={s.Project.Id}{extra}", Ct)).ShouldBeOkAsync<BoardDto>();

    private static BoardColumnDto Column(BoardDto board, IssueStatus status) => board.Columns.Single(c => c.Status == status);

    private static IEnumerable<IssueAction> Moves(BoardCardDto card) => card.Moves.Select(m => m.Action);

    [Fact]
    public async Task Board_HasEveryColumn_AndMovesFollowTheWorkflowForTheCaller()
    {
        var s = await SeedAsync();
        var high = await CreateIssueAsync(s, "Totals wrong");
        var critical = await CreateIssueAsync(s, "Crash on save", "Critical");

        // While testing: the tester may pre-assign (New → Assigned), developers may not move anything.
        var board = await BoardAsync(s.Tester, s);
        board.Columns.Select(c => c.Status).ShouldBe(BoardHandler.ColumnOrder);
        Column(board, IssueStatus.New).Count.ShouldBe(2);
        Column(board, IssueStatus.New).Cards.Select(c => c.Key).ShouldBe([critical.Key, high.Key]); // most severe first
        Column(board, IssueStatus.New).Cards.ShouldAllBe(c => c.Moves.Single().Action == IssueAction.Assign && c.Moves.Single().Target == IssueStatus.Assigned);
        (await BoardAsync(s.Developer, s)).Columns.SelectMany(c => c.Cards).ShouldAllBe(c => c.Moves.Count == 0 && c.SprintStatus == SprintStatus.Testing);

        var sprint = await (await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();

        // Testing completed: developers may pick (→ In Progress); assigning is not a move.
        var newCard = Column(await BoardAsync(s.Developer, s), IssueStatus.New).Cards.First(c => c.Key == high.Key);
        newCard.Moves.ShouldContain(m => m.Action == IssueAction.Pick && m.Target == IssueStatus.InProgress);
        (await s.Developer.PostAsync($"/api/issues/{high.Key}/pick", null, Ct)).EnsureSuccessStatusCode();

        board = await BoardAsync(s.Developer, s);
        var started = Column(board, IssueStatus.InProgress).Cards.Single();
        started.Key.ShouldBe(high.Key);
        started.AssigneeId.ShouldBe(s.DeveloperUser.Id);
        Moves(started).ShouldBe([IssueAction.NeedInfo, IssueAction.Hold, IssueAction.Dispute, IssueAction.MarkFixed], ignoreOrder: true);
        started.Moves.Single(m => m.Action == IssueAction.MarkFixed).Requires.ShouldBe(["reason", "environments"]);

        // The tester sees the same card without developer moves.
        Moves(Column(await BoardAsync(s.Tester, s), IssueStatus.InProgress).Cards.Single()).ShouldBe([IssueAction.Hold]);

        // Filters.
        (await BoardAsync(s.Developer, s, "&mine=true")).Columns.Sum(c => c.Count).ShouldBe(1);
        (await BoardAsync(s.Developer, s, "&unassigned=true")).Columns.Sum(c => c.Count).ShouldBe(1);
        (await BoardAsync(s.Developer, s, "&severity=Critical")).Columns.SelectMany(c => c.Cards).Single().Key.ShouldBe(critical.Key);
        (await BoardAsync(s.Developer, s, "&q=Totals")).Columns.SelectMany(c => c.Cards).Single().Key.ShouldBe(high.Key);
    }

    [Fact]
    public async Task Board_NeedsAnExistingProject()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        (await tester.GetAsync("/api/board", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tester.GetAsync("/api/board?projectId=999999", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
