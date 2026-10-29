using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Analytics;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Environments;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using ActivityKind = QaTracker.Domain.Enums.ActivityKind;

namespace QaTracker.Api.IntegrationTests.Analytics;

public sealed class AnalyticsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, HttpClient Developer, User DeveloperUser, ClientDto Client, ProjectDto Project, ModuleDto Module, EnvironmentDto Uat, EnvironmentDto Live, SprintDto Sprint);

    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var developer = (await api.LoginAsync(developerUser)).Client;
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Analytics client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "A" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
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
        return new Setup(tester, developer, developerUser, client, project, module, uat, live, sprint);
    }

    private static async Task<IssueDto> CreateIssueAsync(Setup s, string title, string severity, int? environmentId) =>
        await (await s.Tester.PostAsJsonAsync("/api/issues", new
        {
            sprintId = s.Sprint.Id,
            moduleId = s.Module.Id,
            foundOnEnvironmentId = environmentId,
            type = "Bug",
            title,
            actualResult = "<p>Wrong.</p>",
            severity,
            priority = "Medium",
        }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static async Task<IssueDto> PostAsync(HttpClient client, string url, object body, string rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        return await (await client.SendAsync(request, Ct)).ShouldBeOkAsync<IssueDto>();
    }

    private static async Task<IssueDto> GetAsync(HttpClient client, string key) =>
        await (await client.GetAsync($"/api/issues/{key}", Ct)).ShouldBeOkAsync<IssueDto>();

    private static async Task<T> GetAsync<T>(HttpClient client, string url) =>
        await (await client.GetAsync(url, Ct)).ShouldBeOkAsync<T>();

    /// <summary>
    /// Four issues: Critical (UAT) fixed, verified and closed; High (LIVE) fixed and reopened by a failed retest;
    /// Medium disputed and rejected as invalid; Low untouched.
    /// </summary>
    private async Task<(IssueDto Critical, IssueDto High, IssueDto Medium, IssueDto Low)> PlayAsync(Setup s)
    {
        var critical = await CreateIssueAsync(s, "Crash on save", "Critical", s.Uat.Id);
        var high = await CreateIssueAsync(s, "Totals wrong", "High", s.Live.Id);
        var medium = await CreateIssueAsync(s, "Label misaligned", "Medium", null);
        var low = await CreateIssueAsync(s, "Typo", "Low", null);

        var sprint = await GetAsync<SprintDetailDto>(s.Tester, $"/api/sprints/{s.Sprint.Id}");
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();

        foreach (var issue in new[] { critical, high, medium })
        {
            (await s.Developer.PostAsync($"/api/issues/{issue.Key}/pick", null, Ct)).EnsureSuccessStatusCode();
        }

        var c = await GetAsync(s.Developer, critical.Key);
        c = await PostAsync(s.Developer, $"/api/issues/{c.Key}/fixed", new { note = "Fixed", environmentIds = new[] { s.Uat.Id } }, c.RowVersion);
        (await s.Tester.PostAsJsonAsync($"/api/deployments/{c.Deployments.Single().Id}/retest", new { passed = true }, Ct)).EnsureSuccessStatusCode();
        c = await GetAsync(s.Tester, c.Key);
        await PostAsync(s.Tester, $"/api/issues/{c.Key}/transition", new { action = "Close" }, c.RowVersion);

        var h = await GetAsync(s.Developer, high.Key);
        h = await PostAsync(s.Developer, $"/api/issues/{h.Key}/fixed", new { note = "Fixed", environmentIds = new[] { s.Uat.Id } }, h.RowVersion);
        (await s.Tester.PostAsJsonAsync($"/api/deployments/{h.Deployments.Single().Id}/retest", new { passed = false, comment = "Still wrong" }, Ct)).EnsureSuccessStatusCode();

        var m = await GetAsync(s.Developer, medium.Key);
        m = await PostAsync(s.Developer, $"/api/issues/{m.Key}/transition", new { action = "Dispute", reason = "As designed" }, m.RowVersion);
        await PostAsync(s.Tester, $"/api/issues/{m.Key}/transition", new { action = "Reject", reason = "Agreed", rejectType = "Invalid" }, m.RowVersion);

        return (critical, high, medium, low);
    }

    private static int Group(IReadOnlyList<StatusGroupCount> groups, StatusGroup group) => groups.Single(g => g.Group == group).Count;

    [Fact]
    public async Task Dashboards_CountTheSeededSprint()
    {
        var s = await SeedAsync();
        var (_, high, _, low) = await PlayAsync(s);
        var scope = $"projectId={s.Project.Id}";

        // Overview (9.1)
        var overview = await GetAsync<OverviewDto>(s.Tester, $"/api/analytics/overview?{scope}");
        overview.Kpis.ShouldBe(new OverviewKpis(Open: 2, OpenWeekAgo: 0, AwaitingRetest: 0, Overdue: 0, CriticalOpen: 0, ClosedThisWeek: 1));
        Group(overview.StatusGroups, StatusGroup.Open).ShouldBe(2);
        Group(overview.StatusGroups, StatusGroup.Closed).ShouldBe(1);
        Group(overview.StatusGroups, StatusGroup.Rejected).ShouldBe(1);
        overview.StatusGroups.Single(g => g.Group == StatusGroup.Open).Statuses.ShouldContain(IssueStatus.Reopened);
        overview.OpenBySeverity.Select(x => x.Count).ShouldBe([0, 1, 0, 1, 0]); // Critical, High, Medium, Low, Not assessed
        overview.CreatedVsClosed.Count.ShouldBe(30);
        overview.CreatedVsClosed[^1].ShouldBe(new DailyFlow(overview.To, 4, 1));
        overview.OpenByProject.Single().ShouldBe(new ProjectCount(s.Project.Id, s.Project.Key, "ERP", 2));
        overview.RecentActivity.Count.ShouldBe(OverviewHandler.RecentActivityCount);
        overview.RecentActivity.ShouldBeInOrder(SortDirection.Descending, Comparer<RecentActivityDto>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt)));
        overview.RecentActivity[0].Summary.ShouldStartWith("Test"); // the tester's rejection – a person, not "System"

        // Sprint dashboard (9.2)
        var dashboard = await GetAsync<SprintDashboardDto>(s.Tester, $"/api/analytics/sprints/{s.Sprint.Id}");
        dashboard.Sprint.IssueCount.ShouldBe(4);
        dashboard.Sprint.Status.ShouldBe(SprintStatus.TestingCompleted);
        Group(dashboard.Progress, StatusGroup.Open).ShouldBe(2);
        dashboard.FoundPerDay.Sum(d => d.Count).ShouldBe(4);
        dashboard.ModuleSeverity.Modules.Single().Name.ShouldBe("Sales");
        dashboard.ModuleSeverity.Cells.Sum(c => c.Count).ShouldBe(4);
        dashboard.ByType.ShouldBe([new TypeCount(IssueType.Bug, 4)]);
        dashboard.Reopened.Count.ShouldBe(1);
        dashboard.Reopened.Items.Single().Key.ShouldBe(high.Key);
        dashboard.Comparison.Single().ShouldSatisfyAllConditions(c => c.Total.ShouldBe(4), c => c.Current.ShouldBeTrue());
        dashboard.Burndown.Points.ShouldNotBeEmpty();
        dashboard.Burndown.Points[0].Ideal.ShouldBe(4); // all four were open when testing completed
        dashboard.Burndown.Points.Last(p => p.Open is not null).Open.ShouldBe(2);

        // Developers (9.3) – two first fixes by the developer, one of them reopened.
        var developers = await GetAsync<DevelopersDto>(s.Developer, $"/api/analytics/developers?{scope}");
        var workload = developers.Workload.Single();
        workload.UserId.ShouldBe(s.DeveloperUser.Id);
        workload.Assigned.ShouldBe(1); // the reopened issue
        developers.FixedPerWeek.Series.Single().Values.Sum().ShouldBe(2);
        developers.FirstTimeRight.Single().ShouldSatisfyAllConditions(m => m.Value.ShouldBe(0.5), m => m.Count.ShouldBe(2));
        developers.AverageFixHours.Single().Count.ShouldBe(2);
        developers.Sla.ShouldBe(new SlaComplianceDto(2, 2, 1));
        developers.Me.ShouldSatisfyAllConditions(m => m.Open.ShouldBe(1), m => m.FixedThisWeek.ShouldBe(2), m => m.AverageFixHours.ShouldNotBeNull());

        // Quality (9.4)
        var quality = await GetAsync<QualityDto>(s.Tester, $"/api/analytics/quality?{scope}");
        quality.Leakage.Single().ByPhase.Single().Count.ShouldBe(4);
        quality.RetestTurnaround.Count.ShouldBe(2);
        quality.RetestTurnaround.ByEnvironmentType.Single().EnvironmentType.ShouldBe(EnvironmentType.UAT);
        quality.RetestAgeing.Sum(a => a.Count).ShouldBe(0);
        quality.ModuleHotspots.Single().ShouldSatisfyAllConditions(m => m.Count.ShouldBe(4), m => m.Open.ShouldBe(2));
        quality.Rejected.ShouldSatisfyAllConditions(r => r.Rejected.ShouldBe(1), r => r.Ratio.ShouldBe(0.25), r => r.ByType.Single(t => t.Type == RejectType.Invalid).Count.ShouldBe(1));
        quality.SeverityTrend.Series.Sum(l => l.Values.Sum()).ShouldBe(4);

        // Client health (9.5)
        var clients = await GetAsync<ClientsDto>(s.Tester, $"/api/analytics/clients?clientId={s.Client.Id}");
        var card = clients.Clients.Single();
        card.ShouldSatisfyAllConditions(c => c.Open.ShouldBe(2), c => c.Critical.ShouldBe(0), c => c.Health.ShouldBe(Health.Green));
        card.LastSprint!.SprintId.ShouldBe(s.Sprint.Id);
        clients.UatVsLive.Single().ShouldBe(new EnvironmentSplitDto(s.Project.Id, s.Project.Key, "ERP", Uat: 3, Live: 1, Other: 0, NotSet: 0)); // no environment given = the sprint default (UAT)
        clients.AverageClosureDays.Single().Count.ShouldBe(1);

        // An overdue issue turns the client amber and shows on the overview.
        await using (var db = api.CreateDbContext())
        {
            await db.Issues.Where(i => i.Id == low.Id).ExecuteUpdateAsync(set => set.SetProperty(i => i.DueDate, DateTimeOffset.UtcNow.AddDays(-1)), Ct);
        }

        (await GetAsync<OverviewDto>(s.Tester, $"/api/analytics/overview?{scope}")).Kpis.Overdue.ShouldBe(1);
        (await GetAsync<ClientsDto>(s.Tester, $"/api/analytics/clients?clientId={s.Client.Id}")).Clients.Single().Health.ShouldBe(Health.Amber);
    }

    [Fact]
    public async Task Filters_AreValidated()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        (await tester.GetAsync("/api/analytics/overview?from=2026-10-10&to=2026-10-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tester.GetAsync("/api/analytics/quality?from=2025-01-01&to=2026-10-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tester.GetAsync("/api/analytics/sprints/999999", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var range = await GetAsync<OverviewDto>(tester, "/api/analytics/overview?from=2026-10-01&to=2026-10-07");
        range.CreatedVsClosed.Select(d => d.Date).ShouldBe(Enumerable.Range(1, 7).Select(d => new DateOnly(2026, 10, d)));
    }

    [Fact]
    public void Health_FollowsThePlanRule()
    {
        ClientsHandler.HealthOf(critical: 1, overdue: 0).ShouldBe(Health.Red);
        ClientsHandler.HealthOf(critical: 0, overdue: 4).ShouldBe(Health.Red);
        ClientsHandler.HealthOf(critical: 0, overdue: 3).ShouldBe(Health.Amber);
        ClientsHandler.HealthOf(critical: 0, overdue: 0).ShouldBe(Health.Green);
        QualityHandler.Bucket(0.5).ShouldBe(AgeingBucket.UnderOneDay);
        QualityHandler.Bucket(2.9).ShouldBe(AgeingBucket.OneToTwoDays);
        QualityHandler.Bucket(5.5).ShouldBe(AgeingBucket.ThreeToFiveDays);
        QualityHandler.Bucket(6).ShouldBe(AgeingBucket.OverFiveDays);
    }

    /// <summary>Plan S21 "done when": every endpoint under 800 ms on 5 000 issues.</summary>
    [Fact]
    public async Task Endpoints_AnswerWithin800ms_On5000Issues()
    {
        var s = await SeedAsync();
        await SeedBulkAsync(s, 5000);
        var urls = new[]
        {
            $"/api/analytics/overview?projectId={s.Project.Id}",
            "/api/analytics/overview",
            $"/api/analytics/sprints/{s.Sprint.Id}",
            $"/api/analytics/developers?projectId={s.Project.Id}",
            $"/api/analytics/quality?projectId={s.Project.Id}",
            "/api/analytics/clients",
        };

        foreach (var url in urls)
        {
            (await s.Tester.GetAsync(url, Ct)).EnsureSuccessStatusCode(); // warm-up (query compilation)
            var watch = Stopwatch.StartNew();
            (await s.Tester.GetAsync(url, Ct)).EnsureSuccessStatusCode();
            watch.ElapsedMilliseconds.ShouldBeLessThan(800, url);
        }
    }

    /// <summary>Issues spread over 60 days with a status history, some fixed with retests (direct inserts – fast).</summary>
    private async Task SeedBulkAsync(Setup s, int count)
    {
        await using var db = api.CreateDbContext();
        var reporterId = await db.Users.Where(u => u.Role == UserRole.Tester).Select(u => u.Id).FirstAsync(Ct);
        var completed = DateTimeOffset.UtcNow.AddDays(-55);
        await db.TestingSprints.Where(x => x.Id == s.Sprint.Id).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Status, SprintStatus.TestingCompleted)
            .SetProperty(x => x.StartedAt, DateTimeOffset.UtcNow.AddDays(-60))
            .SetProperty(x => x.TestingCompletedAt, completed), Ct);

        var severities = Enum.GetValues<Severity>();
        var statuses = new[] { IssueStatus.New, IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.Fixed, IssueStatus.Closed, IssueStatus.Reopened };
        var random = new Random(21);
        var issues = Enumerable.Range(1, count).Select(n =>
        {
            var created = DateTimeOffset.UtcNow.AddDays(-60).AddMinutes(random.Next(0, 60 * 24 * 5));
            var status = statuses[n % statuses.Length];
            var fixedAt = completed.AddHours(random.Next(1, 24 * 40));
            var issue = new Issue
            {
                ProjectId = s.Project.Id,
                Number = 1000 + n,
                IssueKey = $"{s.Project.Key}-{1000 + n}",
                SprintId = s.Sprint.Id,
                ModuleId = s.Module.Id,
                FoundOnEnvironmentId = n % 2 == 0 ? s.Uat.Id : s.Live.Id,
                Type = IssueType.Bug,
                Title = $"Bulk issue {n}",
                ActualResult = "<p>Wrong.</p>",
                Severity = severities[n % severities.Length],
                Priority = Priority.Medium,
                Status = status,
                ReporterId = reporterId,
                AssigneeId = status == IssueStatus.New ? null : s.DeveloperUser.Id,
                DueDate = completed.AddDays(10 + n % 20),
                ReopenCount = status == IssueStatus.Reopened ? 1 : 0,
                CreatedAt = created,
                ClosedAt = status == IssueStatus.Closed ? fixedAt.AddDays(1) : null,
                FirstFixedAt = status is IssueStatus.Fixed or IssueStatus.Closed or IssueStatus.Reopened ? fixedAt : null,
            };
            issue.IssueActivities.Add(new IssueActivity { UserId = reporterId, Kind = ActivityKind.Created, CreatedAt = created });
            if (status != IssueStatus.New)
            {
                issue.IssueActivities.Add(new IssueActivity { UserId = s.DeveloperUser.Id, Kind = ActivityKind.Assigned, NewValue = s.DeveloperUser.Id.ToString(), CreatedAt = completed.AddMinutes(5) });
                issue.IssueActivities.Add(new IssueActivity { UserId = s.DeveloperUser.Id, Kind = ActivityKind.StatusChanged, OldValue = "New", NewValue = "InProgress", CreatedAt = completed.AddMinutes(5) });
            }

            if (issue.FirstFixedAt is not null)
            {
                issue.IssueActivities.Add(new IssueActivity { UserId = s.DeveloperUser.Id, Kind = ActivityKind.StatusChanged, OldValue = "InProgress", NewValue = "Fixed", CreatedAt = fixedAt });
                issue.Deployments.Add(new Deployment
                {
                    EnvironmentId = s.Uat.Id,
                    DeployedById = s.DeveloperUser.Id,
                    DeployedAt = fixedAt,
                    RetestResult = status == IssueStatus.Fixed ? RetestResult.Pending : status == IssueStatus.Reopened ? RetestResult.Failed : RetestResult.Passed,
                    RetestedById = status == IssueStatus.Fixed ? null : reporterId,
                    RetestedAt = status == IssueStatus.Fixed ? null : fixedAt.AddHours(20),
                });
            }

            if (status is IssueStatus.Closed or IssueStatus.Reopened)
            {
                issue.IssueActivities.Add(new IssueActivity { UserId = reporterId, Kind = ActivityKind.StatusChanged, OldValue = "Fixed", NewValue = status.ToString(), CreatedAt = fixedAt.AddDays(1) });
            }

            return issue;
        }).ToList();

        db.Issues.AddRange(issues);
        await db.SaveChangesAsync(Ct);
    }
}
