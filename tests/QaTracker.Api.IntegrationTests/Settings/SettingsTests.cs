using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Notifications;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Settings;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.IntegrationTests.Settings;

/// <summary>
/// Admin settings and the SLA alerts job (S28). Settings are shared by the whole test run: these tests change only the
/// Low SLA (and put it back) and only phases and holidays they create themselves.
/// </summary>
public sealed class SettingsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Tester, ProjectDto Project, SprintDto Sprint)> SeedSprintAsync(params int[] memberIds)
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Settings client {Guid.NewGuid():N}" }, Ct))
            .ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "T" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct))
            .ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        if (memberIds.Length > 0)
        {
            (await tester.PutAsJsonAsync($"/api/projects/{project.Id}/members", new { userIds = memberIds }, Ct)).EnsureSuccessStatusCode();
        }

        var phase = (await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>()).Single(p => p.Name == "UAT");
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1 – UAT", phaseId = phase.Id }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        return (tester, project, sprint);
    }

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

    private static async Task CompleteTestingAsync(HttpClient tester, SprintDto sprint) =>
        (await tester.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete-testing", new { rowVersion = sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();

    [Fact]
    public async Task ChangingTheSla_AffectsNewDueDates()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var policies = await (await admin.GetAsync("/api/settings/sla", Ct)).ShouldBeOkAsync<List<SlaPolicyDto>>();
        policies.Select(p => p.Severity).ShouldBe([Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.NotAssessed]);
        var low = policies.Single(p => p.Severity == Severity.Low);

        (await admin.PutAsJsonAsync("/api/settings/sla", new { policies = new[] { new { severity = "Low", workingHours = 0, rowVersion = low.RowVersion } } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await (await api.ClientForAsync(UserRole.Tester)).GetAsync("/api/settings/sla", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var changed = await (await admin.PutAsJsonAsync("/api/settings/sla", new { policies = new[] { new { severity = "Low", workingHours = 1, rowVersion = low.RowVersion } } }, Ct))
            .ShouldBeOkAsync<List<SlaPolicyDto>>();
        try
        {
            changed.Single(p => p.Severity == Severity.Low).WorkingHours.ShouldBe(1);
            await (await admin.PutAsJsonAsync("/api/settings/sla", new { policies = new[] { new { severity = "Low", workingHours = 2, rowVersion = low.RowVersion } } }, Ct))
                .ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency");

            var reporter = await api.CreateUserAsync(UserRole.Tester);
            var (tester, _, sprint) = await SeedSprintAsync();
            var issue = await AddIssueAsync(sprint, Severity.Low, reporter.Id);
            var before = DateTimeOffset.UtcNow;
            await CompleteTestingAsync(tester, sprint);

            await using var db = api.CreateDbContext();
            var due = (await db.Issues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, Ct)).DueDate!.Value;
            // 1 working hour (moved off a Sunday or holiday) instead of the seeded 28 working days.
            due.ShouldBeGreaterThan(before);
            due.ShouldBeLessThan(before.AddDays(4));
        }
        finally
        {
            var current = changed.Single(p => p.Severity == Severity.Low);
            (await admin.PutAsJsonAsync("/api/settings/sla", new { policies = new[] { new { severity = "Low", workingHours = low.WorkingHours, rowVersion = current.RowVersion } } }, Ct))
                .EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Holidays_CanBeAddedOnceAndRemoved()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var date = new DateOnly(2099, 1, 1).AddDays(Random.Shared.Next(0, 3000));

        var created = await (await admin.PostAsJsonAsync("/api/settings/holidays", new { date, name = "  Founders day " }, Ct))
            .ShouldBeOkAsync<HolidayDto>(HttpStatusCode.Created);
        created.Name.ShouldBe("Founders day");
        await (await admin.PostAsJsonAsync("/api/settings/holidays", new { date, name = "Again" }, Ct)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "holiday_exists");
        (await (await admin.GetAsync("/api/settings/holidays", Ct)).ShouldBeOkAsync<List<HolidayDto>>()).ShouldContain(created);

        (await admin.DeleteAsync($"/api/settings/holidays/{date:yyyy-MM-dd}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/settings/holidays/{date:yyyy-MM-dd}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Phases_CanBeAddedReorderedRenamedAndHidden()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var first = await (await admin.PostAsJsonAsync("/api/settings/phases", new { name = $"Pilot {suffix}" }, Ct)).ShouldBeOkAsync<PhaseDto>(HttpStatusCode.Created);
        var second = await (await admin.PostAsJsonAsync("/api/settings/phases", new { name = $"Hypercare {suffix}" }, Ct)).ShouldBeOkAsync<PhaseDto>(HttpStatusCode.Created);
        second.SortOrder.ShouldBe(first.SortOrder + 1);
        await (await admin.PostAsJsonAsync("/api/settings/phases", new { name = $"pilot {suffix}" }, Ct)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "phase_name_taken");

        // Swap the two new phases (both last, so other tests' "first phase" is untouched).
        var all = await (await admin.GetAsync("/api/settings/phases", Ct)).ShouldBeOkAsync<List<PhaseDto>>();
        var order = all.Where(p => p.Id != first.Id && p.Id != second.Id).Append(second).Append(first).Select(p => new { id = p.Id, rowVersion = p.RowVersion }).ToList();
        var reordered = await (await admin.PutAsJsonAsync("/api/settings/phases/order", new { phases = order }, Ct)).ShouldBeOkAsync<List<PhaseDto>>();
        reordered.Select(p => p.Id).TakeLast(2).ShouldBe([second.Id, first.Id]);
        await (await admin.PutAsJsonAsync("/api/settings/phases/order", new { phases = order.Take(1) }, Ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest);

        var current = reordered.Single(p => p.Id == first.Id);
        var renamed = await (await admin.PutAsJsonAsync($"/api/settings/phases/{first.Id}", new { name = $"Pilot run {suffix}", isActive = false, rowVersion = current.RowVersion }, Ct))
            .ShouldBeOkAsync<PhaseDto>();
        (renamed.Name, renamed.IsActive).ShouldBe(($"Pilot run {suffix}", false));
        await (await admin.PutAsJsonAsync($"/api/settings/phases/{first.Id}", new { name = "X", isActive = true, rowVersion = current.RowVersion }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency");

        var lookup = await (await admin.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        lookup.ShouldNotContain(p => p.Id == first.Id);
        lookup.ShouldContain(p => p.Id == second.Id);
    }

    [Fact]
    public async Task SlaAlerts_FireOncePerIssue_AndOverdueAgainForANewDueDate()
    {
        var reporter = await api.CreateUserAsync(UserRole.Tester);
        var developer = await api.CreateUserAsync(UserRole.Developer);
        var member = await api.CreateUserAsync(UserRole.Developer);
        var (tester, _, sprint) = await SeedSprintAsync(developer.Id, member.Id);
        var late = await AddIssueAsync(sprint, Severity.High, reporter.Id, developer.Id);
        var critical = await AddIssueAsync(sprint, Severity.Critical, reporter.Id);
        var justUnassigned = await AddIssueAsync(sprint, Severity.Critical, reporter.Id);
        await CompleteTestingAsync(tester, sprint);

        var now = DateTimeOffset.UtcNow;
        await using (var db = api.CreateDbContext())
        {
            await db.TestingSprints.Where(s => s.Id == sprint.Id).ExecuteUpdateAsync(set => set.SetProperty(s => s.TestingCompletedAt, now.AddHours(-3)), Ct);
            await db.Issues.Where(i => i.Id == late.Id).ExecuteUpdateAsync(set => set.SetProperty(i => i.DueDate, now.AddHours(-1)), Ct);
            db.IssueActivities.Add(new IssueActivity
            {
                IssueId = justUnassigned.Id, UserId = reporter.Id, Kind = ActivityKind.Assigned, OldValue = developer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), CreatedAt = now.AddMinutes(-30),
            });
            await db.SaveChangesAsync(Ct);
        }

        await RunAlertsAsync();
        await RunAlertsAsync();

        await using (var db = api.CreateDbContext())
        {
            var ours = new[] { late.Id, critical.Id, justUnassigned.Id };
            var alerts = await db.Notifications.AsNoTracking()
                .Where(n => n.IssueId != null && ours.Contains(n.IssueId.Value) && (n.Type == NotificationType.Overdue || n.Type == NotificationType.CriticalUnassigned))
                .ToListAsync(Ct);
            alerts.Where(a => a.Type == NotificationType.Overdue).Select(a => (a.IssueId, a.UserId)).ShouldBe([(late.Id, developer.Id)]);
            alerts.Where(a => a.Type == NotificationType.CriticalUnassigned).Select(a => (a.IssueId, a.UserId))
                .ShouldBe([(critical.Id, developer.Id), (critical.Id, member.Id)], ignoreOrder: true);

            // Critical unassigned is mailed at once; overdue waits for the daily summary.
            var mails = await db.EmailOutboxes.AsNoTracking().Where(m => m.ToAddress == member.Email || m.ToAddress == developer.Email).Select(m => m.Subject).ToListAsync(Ct);
            mails.Count(s => s.StartsWith($"Critical {critical.IssueKey}", StringComparison.Ordinal)).ShouldBe(2);
            mails.ShouldNotContain(s => s.Contains("overdue", StringComparison.OrdinalIgnoreCase));

            // A later due date that passes alerts again.
            await db.Notifications.Where(n => n.IssueId == late.Id && n.Type == NotificationType.Overdue)
                .ExecuteUpdateAsync(set => set.SetProperty(n => n.CreatedAt, now.AddHours(-2)), Ct);
            await db.Issues.Where(i => i.Id == late.Id).ExecuteUpdateAsync(set => set.SetProperty(i => i.DueDate, now.AddMinutes(-10)), Ct);
        }

        await RunAlertsAsync();
        await using (var db = api.CreateDbContext())
        {
            (await db.Notifications.CountAsync(n => n.IssueId == late.Id && n.Type == NotificationType.Overdue, Ct)).ShouldBe(2);
            (await db.JobRuns.AsNoTracking().SingleAsync(j => j.Name == SlaAlertsHandler.JobName, Ct)).LastResult.ShouldNotBeNull();
        }
    }

    private async Task RunAlertsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SlaAlertsHandler>().HandleAsync(Ct);
    }
}
