using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Issues;

/// <summary>Plan 15.3 / S31 "done when": the issue list answers in under 500 ms with 1 000 rows matching the filter.</summary>
public sealed class IssueListPerformanceTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IssueList_AnswersWithin500ms_With1000MatchingRows()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Perf client {Guid.NewGuid():N}" }, Ct)).ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "P" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct)).ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var phase = (await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>())[0];
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1", phaseId = phase.Id }, Ct)).ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);

        // 3 000 issues: every third one is High + Assigned (1 000 match the filter below).
        await using (var db = api.CreateDbContext())
        {
            var reporter = db.Users.First(u => u.Role == UserRole.Tester).Id;
            var start = DateTimeOffset.UtcNow.AddDays(-30);
            db.Issues.AddRange(Enumerable.Range(1, 3000).Select(n => new Issue
            {
                ProjectId = project.Id,
                Number = n,
                IssueKey = $"{key}-{n}",
                SprintId = sprint.Id,
                ModuleId = module.Id,
                Type = IssueType.Bug,
                Title = $"Invoice line {n} total wrong",
                ScreenName = $"Screen {n % 40}",
                ActualResult = "<p>Wrong.</p>",
                Severity = n % 3 == 0 ? Severity.High : Severity.Low,
                Priority = Priority.Medium,
                Status = n % 3 == 0 ? IssueStatus.Assigned : IssueStatus.New,
                ReporterId = reporter,
                DueDate = start.AddDays(20 + (n % 15)),
                CreatedAt = start.AddMinutes(n),
            }));
            await db.SaveChangesAsync(Ct);
        }

        var urls = new[]
        {
            $"/api/issues?projectId={project.Id}&status=Assigned&severity=High",
            $"/api/issues?projectId={project.Id}&status=Assigned&severity=High&sort=-dueDate&page=20",
            $"/api/issues?clientId={client.Id}&q=total&sort=title",
            $"/api/issues?sprintId={sprint.Id}&size=100",
        };

        foreach (var url in urls)
        {
            var first = await (await tester.GetAsync(url, Ct)).ShouldBeOkAsync<PagedResult<IssueListItemDto>>(); // warm-up
            first.TotalCount.ShouldBeGreaterThanOrEqualTo(1000, url);

            var watch = Stopwatch.StartNew();
            (await tester.GetAsync(url, Ct)).EnsureSuccessStatusCode();
            watch.ElapsedMilliseconds.ShouldBeLessThan(500, url);
        }
    }
}
