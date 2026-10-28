using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Sprints;

public sealed class SprintsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UniqueKey() => "S" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    private async Task<(HttpClient Tester, ProjectDto Project, PhaseLookup Uat)> SeedProjectAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Sprint client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key = UniqueKey() }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        return (tester, project, phases.Single(p => p.Name == "UAT"));
    }

    private static async Task<SprintDto> StartAsync(HttpClient client, int projectId, string name, int phaseId) =>
        await (await client.PostAsJsonAsync($"/api/projects/{projectId}/sprints", new { name, phaseId }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);

    /// <summary>Issues are created through the API from S09; here they are inserted directly.</summary>
    private async Task<Issue> AddIssueAsync(SprintDto sprint, Severity severity, int reporterId, int? assigneeId = null)
    {
        await using var db = api.CreateDbContext();
        var module = await db.Modules.FirstOrDefaultAsync(m => m.ProjectId == sprint.ProjectId, Ct)
            ?? db.Modules.Add(new Module { ProjectId = sprint.ProjectId, Name = "Sales", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, CreatedById = reporterId }).Entity;
        var number = await db.Issues.CountAsync(i => i.ProjectId == sprint.ProjectId, Ct) + 1;
        var issue = new Issue
        {
            ProjectId = sprint.ProjectId,
            Number = number,
            IssueKey = $"{sprint.ProjectKey}-{number}",
            SprintId = sprint.Id,
            Module = module,
            Type = IssueType.Bug,
            Title = $"Issue {number}",
            ActualResult = "Wrong total",
            Severity = severity,
            Priority = Priority.Medium,
            Status = assigneeId is null ? IssueStatus.New : IssueStatus.Assigned,
            ReporterId = reporterId,
            AssigneeId = assigneeId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync(Ct);
        return issue;
    }

    [Fact]
    public async Task SecondTestingSprint_ForTheSameProject_Is422()
    {
        var (tester, project, uat) = await SeedProjectAsync();

        var suggestion = await (await tester.GetAsync($"/api/projects/{project.Id}/sprints/suggested-name?phaseId={uat.Id}", Ct))
            .ShouldBeOkAsync<SprintNameSuggestion>();
        suggestion.Name.ShouldBe("Sprint 1 – UAT");

        var sprint = await StartAsync(tester, project.Id, suggestion.Name, uat.Id);
        sprint.Status.ShouldBe(SprintStatus.Testing);
        sprint.PhaseName.ShouldBe("UAT");

        var second = await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 2 – UAT", phaseId = uat.Id }, Ct);
        var problem = await second.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
        problem.Code().ShouldBe("sprint_already_testing");
        problem.GetProperty("detail").GetString()!.ShouldContain("Sprint 1 – UAT");
    }

    [Fact]
    public async Task Developer_CannotStartOrComplete_ButCanRead()
    {
        var (tester, project, uat) = await SeedProjectAsync();
        var sprint = await StartAsync(tester, project.Id, "Sprint 1 – UAT", uat.Id);
        var developer = await api.ClientForAsync(UserRole.Developer);

        (await developer.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "X", phaseId = uat.Id }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await developer.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete-testing", new { rowVersion = sprint.RowVersion }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var detail = await (await developer.GetAsync($"/api/sprints/{sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        detail.Events.Single().Kind.ShouldBe(SprintEventKind.Started);
        (await (await developer.GetAsync($"/api/projects/{project.Id}/sprints", Ct)).ShouldBeOkAsync<List<SprintDto>>()).Single().Id.ShouldBe(sprint.Id);
    }

    [Fact]
    public async Task CompleteTesting_SetsDueDates_WritesHistory_AndNotifies()
    {
        var (tester, project, uat) = await SeedProjectAsync();
        var sprint = await StartAsync(tester, project.Id, "Sprint 1 – UAT", uat.Id);
        var reporter = await api.CreateUserAsync(UserRole.Tester);
        var developer = await api.CreateUserAsync(UserRole.Developer);
        var member = await api.CreateUserAsync(UserRole.Developer);
        (await tester.PutAsJsonAsync($"/api/projects/{project.Id}/members", new { userIds = new[] { member.Id } }, Ct)).EnsureSuccessStatusCode();

        var high = await AddIssueAsync(sprint, Severity.High, reporter.Id, developer.Id);
        var notAssessed = await AddIssueAsync(sprint, Severity.NotAssessed, reporter.Id);

        var summary = await (await tester.GetAsync($"/api/sprints/{sprint.Id}/completion-summary", Ct)).ShouldBeOkAsync<CompletionSummaryDto>();
        (summary.IssueCount, summary.UnassignedCount, summary.NotAssessedCount).ShouldBe((2, 1, 1));
        summary.ByModule.Single().Count.ShouldBe(2);

        var before = DateTimeOffset.UtcNow;
        var completed = await (await tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete-testing", new { rowVersion = sprint.RowVersion, note = "All modules covered" }, Ct))
            .ShouldBeOkAsync<SprintDto>();

        completed.Status.ShouldBe(SprintStatus.TestingCompleted);
        completed.TestingCompletedAt.ShouldNotBeNull();

        await using var db = api.CreateDbContext();
        var issues = await db.Issues.AsNoTracking().Where(i => i.SprintId == sprint.Id).ToDictionaryAsync(i => i.Id, Ct);
        // High = 14 working days, Not assessed = 35 (ADR-016) – at least that many calendar days later.
        issues[high.Id].DueDate!.Value.ShouldBeGreaterThan(before.AddDays(14));
        issues[notAssessed.Id].DueDate!.Value.ShouldBeGreaterThan(issues[high.Id].DueDate!.Value);

        (await db.IssueActivities.CountAsync(a => issues.Keys.Contains(a.IssueId) && a.Kind == ActivityKind.SprintEvent, Ct)).ShouldBe(2);
        var recipients = await db.Notifications.Where(n => n.SprintId == sprint.Id && n.Type == NotificationType.TestingCompleted)
            .Select(n => n.UserId).ToListAsync(Ct);
        recipients.ShouldBe([developer.Id, member.Id], ignoreOrder: true);

        var detail = await (await tester.GetAsync($"/api/sprints/{sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        detail.Events.Select(e => e.Kind).ShouldBe([SprintEventKind.Started, SprintEventKind.TestingCompleted]);
        detail.Events[1].Note.ShouldBe("All modules covered");

        // The project is free for the next sprint, and completing twice is refused.
        (await StartAsync(tester, project.Id, "Sprint 1 – UAT Round 2", uat.Id)).Status.ShouldBe(SprintStatus.Testing);
        (await (await tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete-testing", new { rowVersion = completed.RowVersion }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("sprint_not_testing");
    }

    [Fact]
    public async Task CompleteTesting_WithAStaleRowVersion_Is409()
    {
        var (tester, project, uat) = await SeedProjectAsync();
        var sprint = await StartAsync(tester, project.Id, "Sprint 1 – UAT", uat.Id);

        await using (var db = api.CreateDbContext())
        {
            var entity = await db.TestingSprints.SingleAsync(s => s.Id == sprint.Id, Ct);
            entity.Notes = "touched";
            await db.SaveChangesAsync(Ct);
        }

        (await (await tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete-testing", new { rowVersion = sprint.RowVersion }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("concurrency");
    }

    [Fact]
    public async Task ReopenTesting_IsAdminOnly_NeedsANote_AndRespectsTheOneTestingSprintRule()
    {
        var (tester, project, uat) = await SeedProjectAsync();
        var admin = await api.ClientForAsync(UserRole.Admin);
        var sprint = await StartAsync(tester, project.Id, "Sprint 1 – UAT", uat.Id);
        var completed = await (await tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete-testing", new { rowVersion = sprint.RowVersion }, Ct))
            .ShouldBeOkAsync<SprintDto>();

        (await tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/reopen-testing", new { rowVersion = completed.RowVersion, note = "Missed a module" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await (await admin.PostAsJsonAsync($"/api/sprints/{sprint.Id}/reopen-testing", new { rowVersion = completed.RowVersion, note = "" }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("note", out _).ShouldBeTrue();

        var next = await StartAsync(tester, project.Id, "Sprint 2 – UAT", uat.Id);
        (await (await admin.PostAsJsonAsync($"/api/sprints/{sprint.Id}/reopen-testing", new { rowVersion = completed.RowVersion, note = "Missed a module" }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("sprint_already_testing");

        await (await tester.PostAsJsonAsync($"/api/sprints/{next.Id}/complete-testing", new { rowVersion = next.RowVersion }, Ct)).ShouldBeOkAsync<SprintDto>();
        var reopened = await (await admin.PostAsJsonAsync($"/api/sprints/{sprint.Id}/reopen-testing", new { rowVersion = completed.RowVersion, note = "Missed a module" }, Ct))
            .ShouldBeOkAsync<SprintDto>();

        reopened.Status.ShouldBe(SprintStatus.Testing);
        reopened.TestingCompletedAt.ShouldBeNull();
        var detail = await (await admin.GetAsync($"/api/sprints/{sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        detail.Events.Last().Kind.ShouldBe(SprintEventKind.ReopenedForTesting);
        detail.Events.Last().Note.ShouldBe("Missed a module");
    }

    [Fact]
    public async Task Start_ValidatesPhaseAndEnvironment()
    {
        var (tester, project, uat) = await SeedProjectAsync();
        var (_, other, _) = await SeedProjectAsync();
        var foreignEnvironment = await (await tester.PostAsJsonAsync($"/api/projects/{other.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.example.com" }, Ct))
            .ShouldBeOkAsync<Application.Features.Environments.EnvironmentDto>(HttpStatusCode.Created);

        (await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "S", phaseId = 999999 }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("phaseId", out _).ShouldBeTrue();
        (await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "S", phaseId = uat.Id, defaultEnvironmentId = foreignEnvironment.Id }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("defaultEnvironmentId", out _).ShouldBeTrue();
    }
}
