using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Application.Features.Timeline;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Sprints;

/// <summary>Sprint close, carry forward and the frozen report (S29, ADR-039).</summary>
public sealed class SprintCloseTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, ProjectDto Project, PhaseLookup Uat, User Reporter, User Developer);

    private async Task<Setup> SeedProjectAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Close client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "C" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        return new Setup(tester, project, phases.Single(p => p.Name == "UAT"), await api.CreateUserAsync(UserRole.Tester), await api.CreateUserAsync(UserRole.Developer));
    }

    private static async Task<SprintDto> StartAsync(Setup s, string name) =>
        await (await s.Tester.PostAsJsonAsync($"/api/projects/{s.Project.Id}/sprints", new { name, phaseId = s.Uat.Id }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);

    private static async Task<SprintDto> CompleteAsync(Setup s, SprintDto sprint) =>
        await (await s.Tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete-testing", new { rowVersion = sprint.RowVersion }, Ct))
            .ShouldBeOkAsync<SprintDto>();

    private async Task<Issue> AddIssueAsync(Setup s, SprintDto sprint, IssueStatus status, Severity severity = Severity.High, int? assigneeId = null)
    {
        await using var db = api.CreateDbContext();
        var module = await db.Modules.FirstOrDefaultAsync(m => m.ProjectId == sprint.ProjectId, Ct)
            ?? db.Modules.Add(new Module { ProjectId = sprint.ProjectId, Name = "Sales", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, CreatedById = s.Reporter.Id }).Entity;
        var number = await db.Issues.CountAsync(i => i.ProjectId == sprint.ProjectId, Ct) + 1;
        var issue = db.Issues.Add(new Issue
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
            Status = status,
            RejectType = status == IssueStatus.Rejected ? RejectType.Invalid : null,
            ReporterId = s.Reporter.Id,
            AssigneeId = assigneeId,
            CreatedAt = DateTimeOffset.UtcNow,
        }).Entity;
        await db.SaveChangesAsync(Ct);
        return issue;
    }

    private async Task<object[]> VersionsAsync(params Issue[] issues)
    {
        await using var db = api.CreateDbContext();
        var ids = issues.Select(i => i.Id).ToList();
        var rows = await db.Issues.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.RowVersion }).ToListAsync(Ct);
        return rows.Select(r => (object)new { id = r.Id, rowVersion = Convert.ToBase64String(r.RowVersion) }).ToArray();
    }

    [Fact]
    public async Task Close_CarriesOpenIssuesForward_FreezesTheSnapshot_AndTheReportKeepsTheNumbersAsOfClosing()
    {
        var s = await SeedProjectAsync();
        var sprint = await StartAsync(s, "Sprint 1 – UAT");
        var done = await AddIssueAsync(s, sprint, IssueStatus.Closed, Severity.High);
        var assigned = await AddIssueAsync(s, sprint, IssueStatus.Assigned, Severity.Critical, s.Developer.Id);
        var fresh = await AddIssueAsync(s, sprint, IssueStatus.New, Severity.Medium);
        var rejected = await AddIssueAsync(s, sprint, IssueStatus.Rejected, Severity.Low);
        var completed = await CompleteAsync(s, sprint);
        var next = await StartAsync(s, "Sprint 2 – UAT");

        // Open issues block closing; a developer may not close or carry forward.
        var blocked = await (await s.Tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/close", new { rowVersion = completed.RowVersion }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
        blocked.Code().ShouldBe("issues_unfinished");
        var developer = (await api.LoginAsync(s.Developer)).Client;
        (await developer.PostAsJsonAsync($"/api/sprints/{sprint.Id}/close", new { rowVersion = completed.RowVersion }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await developer.PostAsJsonAsync($"/api/sprints/{sprint.Id}/carry-forward", new { targetSprintId = next.Id, issues = await VersionsAsync(fresh) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Carrying only some of the open issues still leaves the sprint unclosable; finished ones cannot be carried.
        (await (await s.Tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/close", new { rowVersion = completed.RowVersion, carryForward = new { targetSprintId = next.Id, issues = await VersionsAsync(fresh) } }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("issues_unfinished");
        (await (await s.Tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/carry-forward", new { targetSprintId = next.Id, issues = await VersionsAsync(done) }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("issue_finished");
        (await (await s.Tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/carry-forward", new { targetSprintId = sprint.Id, issues = await VersionsAsync(fresh) }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("same_sprint");

        var open = await (await s.Tester.GetAsync($"/api/sprints/{sprint.Id}/open-issues", Ct)).ShouldBeOkAsync<List<OpenIssueDto>>();
        open.Select(i => i.Key).ShouldBe([assigned.IssueKey, fresh.IssueKey]);
        open[0].AssigneeName.ShouldBe(s.Developer.FullName);

        var closed = await (await s.Tester.PostAsJsonAsync(
                $"/api/sprints/{sprint.Id}/close",
                new { rowVersion = completed.RowVersion, note = "Round 1 done", carryForward = new { targetSprintId = next.Id, issues = open.Select(i => new { i.Id, i.RowVersion }) } },
                Ct))
            .ShouldBeOkAsync<SprintDto>();

        closed.Status.ShouldBe(SprintStatus.Closed);
        closed.ClosedAt.ShouldNotBeNull();
        closed.IssueCount.ShouldBe(2);
        closed.OpenIssueCount.ShouldBe(0);

        await using (var db = api.CreateDbContext())
        {
            var moved = await db.Issues.AsNoTracking().Where(i => i.Id == assigned.Id || i.Id == fresh.Id).ToListAsync(Ct);
            moved.ShouldAllBe(i => i.SprintId == next.Id && i.CarriedFromSprintId == sprint.Id && i.DueDate == null);
            moved.Select(i => i.IssueKey).ShouldBe([assigned.IssueKey, fresh.IssueKey], ignoreOrder: true);

            var snapshot = await db.SprintSnapshots.AsNoTracking().Where(x => x.SprintId == sprint.Id).ToDictionaryAsync(x => x.IssueId, Ct);
            snapshot.Count.ShouldBe(4);
            snapshot[assigned.Id].StatusAtClose.ShouldBe(IssueStatus.Assigned);
            snapshot[assigned.Id].AssigneeAtCloseId.ShouldBe(s.Developer.Id);
            snapshot[assigned.Id].CarriedToSprintId.ShouldBe(next.Id);
            snapshot[done.Id].CarriedToSprintId.ShouldBeNull();

            var kinds = await db.IssueActivities.AsNoTracking()
                .Where(a => a.IssueId == assigned.Id || a.IssueId == done.Id)
                .Select(a => new { a.IssueId, a.Kind, a.OldValue, a.NewValue, a.Body })
                .ToListAsync(Ct);
            kinds.ShouldContain(a => a.IssueId == assigned.Id && a.Kind == ActivityKind.CarriedForward && a.OldValue == "Sprint 1 – UAT" && a.NewValue == "Sprint 2 – UAT" && a.Body == "Round 1 done");
            kinds.ShouldContain(a => a.IssueId == done.Id && a.Kind == ActivityKind.SprintEvent && a.Body == "Sprint 1 – UAT closed.");
        }

        var history = await (await s.Tester.GetAsync($"/api/sprints/{sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        history.Events.Select(e => e.Kind).ShouldBe([SprintEventKind.Started, SprintEventKind.TestingCompleted, SprintEventKind.IssuesCarriedForward, SprintEventKind.Closed]);
        history.Events[2].Note!.ShouldStartWith($"2 issues carried forward to Sprint 2 – UAT: {assigned.IssueKey}, {fresh.IssueKey}.");
        var target = await (await s.Tester.GetAsync($"/api/sprints/{next.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        target.Events.Last().Kind.ShouldBe(SprintEventKind.IssuesCarriedIn);
        target.Sprint.IssueCount.ShouldBe(2);

        var timeline = await (await s.Tester.GetAsync($"/api/issues/{assigned.IssueKey}/activities", Ct)).ShouldBeOkAsync<TimelinePage>();
        timeline.Items.ShouldContain(i => i.Kind == ActivityKind.CarriedForward && i.Summary!.Contains("carried the issue forward from Sprint 1 – UAT to Sprint 2 – UAT"));

        // The issues move on: the carried one is fixed and closed in the next sprint, someone edits a closed one.
        await using (var db = api.CreateDbContext())
        {
            await db.Issues.Where(i => i.Id == assigned.Id).ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, IssueStatus.Closed).SetProperty(i => i.AssigneeId, (int?)null), Ct);
            await db.Issues.Where(i => i.Id == done.Id).ExecuteUpdateAsync(u => u.SetProperty(i => i.Severity, Severity.Low), Ct);
        }

        var report = await s.Tester.GetAsync($"/api/sprints/{sprint.Id}/report?format=xlsx", Ct);
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var book = new XLWorkbook(new MemoryStream(await report.Content.ReadAsByteArrayAsync(Ct)));
        var all = book.Worksheet("All issues");
        var rows = all.RowsUsed().Skip(1).ToDictionary(r => r.Cell(1).GetString());
        rows.Count.ShouldBe(4);
        rows[assigned.IssueKey].Cell(8).GetString().ShouldBe("Assigned");
        rows[assigned.IssueKey].Cell(9).GetString().ShouldBe(s.Developer.FullName);
        rows[assigned.IssueKey].Cell(18).GetString().ShouldBe("Sprint 2 – UAT");
        rows[done.IssueKey].Cell(6).GetString().ShouldBe("High");
        rows[done.IssueKey].Cell(18).GetString().ShouldBeEmpty();
        book.Worksheet("Summary").CellsUsed().ShouldContain(c => c.GetString().StartsWith("2 issue(s)"));

        // Closed is final: no second close, nothing more to carry, and the carried issue stays out of the bin.
        (await (await s.Tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/close", new { rowVersion = closed.RowVersion }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("sprint_not_testing_completed");
        (await (await s.Tester.DeleteAsync($"/api/issues/{fresh.IssueKey}", Ct))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("issue_carried_forward");
        _ = rejected;
    }

    [Fact]
    public async Task CarryForward_IntoACompletedSprint_KeepsTheDueDate_And409OnAStaleIssue()
    {
        var s = await SeedProjectAsync();
        var first = await StartAsync(s, "Sprint 1 – UAT");
        var issue = await AddIssueAsync(s, first, IssueStatus.New);
        var other = await AddIssueAsync(s, first, IssueStatus.New);
        await CompleteAsync(s, first);
        var second = await StartAsync(s, "Sprint 2 – UAT");
        await CompleteAsync(s, second);
        var versions = await VersionsAsync(issue);

        var result = await (await s.Tester.PostAsJsonAsync($"/api/sprints/{first.Id}/carry-forward", new { targetSprintId = second.Id, issues = versions }, Ct))
            .ShouldBeOkAsync<CarryForwardResult>();

        result.CarriedCount.ShouldBe(1);
        result.Sprint.IssueCount.ShouldBe(1);
        result.Sprint.CarriedForwardCount.ShouldBe(1);
        result.Target.IssueCount.ShouldBe(1);
        await using (var db = api.CreateDbContext())
        {
            (await db.Issues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, Ct)).DueDate.ShouldNotBeNull();
        }

        // Same version again (the issue changed with the move), an unknown sprint, an empty list.
        (await (await s.Tester.PostAsJsonAsync($"/api/sprints/{second.Id}/carry-forward", new { targetSprintId = first.Id, issues = versions }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("concurrency");
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{first.Id}/carry-forward", new { targetSprintId = 999999, issues = await VersionsAsync(other) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await s.Tester.PostAsJsonAsync($"/api/sprints/{first.Id}/carry-forward", new { targetSprintId = second.Id, issues = Array.Empty<object>() }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("issues", out _).ShouldBeTrue();
    }
}
