using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.EmailAdmin;
using QaTracker.Application.Features.Environments;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Notifications;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Infrastructure.Email;

namespace QaTracker.Api.IntegrationTests.Notifications;

public sealed class EmailTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, User TesterUser, HttpClient Developer, User DeveloperUser, ModuleDto Module, SprintDto Sprint, EnvironmentDto Uat);

    private async Task<Setup> SeedAsync()
    {
        var testerUser = await api.CreateUserAsync(UserRole.Tester);
        var tester = (await api.LoginAsync(testerUser)).Client;
        var developerUser = await api.CreateUserAsync(UserRole.Developer);
        var developer = (await api.LoginAsync(developerUser)).Client;
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Mail client {Guid.NewGuid():N}" }, Ct)).ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "M" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct)).ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Billing" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var uat = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new { name = "UAT 1", type = "UAT", url = "https://uat.example.com" }, Ct))
            .ShouldBeOkAsync<EnvironmentDto>(HttpStatusCode.Created);
        var phases = await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>();
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1", phaseId = phases[0].Id, defaultEnvironmentId = uat.Id }, Ct))
            .ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        return new Setup(tester, testerUser, developer, developerUser, module, sprint, uat);
    }

    private static async Task<IssueDto> CreateIssueAsync(Setup s, string title, int? assigneeId = null) =>
        await (await s.Tester.PostAsJsonAsync("/api/issues", new
        {
            sprintId = s.Sprint.Id, moduleId = s.Module.Id, type = "Bug", title, actualResult = "<p>Wrong.</p>", severity = "High", priority = "High", assigneeId,
        }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);

    private static async Task CompleteTestingAsync(Setup s)
    {
        var sprint = await (await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task AssignAsync(Setup s, string key, int userId)
    {
        var issue = await (await s.Tester.GetAsync($"/api/issues/{key}", Ct)).ShouldBeOkAsync<IssueDto>();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/issues/{key}/assign") { Content = JsonContent.Create(new { userId }) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{issue.RowVersion}\""));
        (await s.Tester.SendAsync(request, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task CommentAsync(HttpClient client, string key, string body)
    {
        var form = new MultipartFormDataContent { { new StringContent(body), "body" } };
        (await client.PostAsync($"/api/issues/{key}/comments", form, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task<List<EmailOutbox>> MailsToAsync(string address)
    {
        await using var db = api.CreateDbContext();
        return await db.EmailOutboxes.AsNoTracking().Where(m => m.ToAddress == address).OrderBy(m => m.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task InstantMails_AreQueuedWithTheNotification_AndRespectPreferences()
    {
        var s = await SeedAsync();
        var first = await CreateIssueAsync(s, "Invoice total ignores the discount", s.DeveloperUser.Id);
        var second = await CreateIssueAsync(s, "Credit note rounds wrong");
        var third = await CreateIssueAsync(s, "Third one");
        await CompleteTestingAsync(s);

        // Testing completed: one summary mail for the pre-assigned developer.
        var mails = await MailsToAsync(s.DeveloperUser.Email);
        var summary = mails.ShouldHaveSingleItem();
        summary.Subject.ShouldContain("testing of Sprint 1 completed");
        summary.Body.ShouldContain("3 issues are ready for development");
        summary.SentAt.ShouldBeNull();

        // Comments go to the daily digest, not instantly.
        await CommentAsync(s.Tester, first.Key, "Customer ACME, order 1042 <script>x</script>");
        (await MailsToAsync(s.DeveloperUser.Email)).Count.ShouldBe(1);

        await AssignAsync(s, second.Key, s.DeveloperUser.Id);
        var assigned = (await MailsToAsync(s.DeveloperUser.Email)).Last();
        assigned.Subject.ShouldBe($"Test Tester assigned {second.Key} to you");
        assigned.Body.ShouldContain($"http://localhost:4200/issues/{second.Key}");
        assigned.Body.ShouldContain("Credit note rounds wrong");
        assigned.Body.ShouldContain("/settings/notifications");

        await using (var db = api.CreateDbContext())
        {
            var notification = await db.Notifications.AsNoTracking().Where(n => n.UserId == s.DeveloperUser.Id && n.Type == NotificationType.Assigned).SingleAsync(Ct);
            notification.EmailedAt.ShouldNotBeNull();
        }

        // Opt out of assignment mails: in-app only.
        var settings = await (await s.Developer.GetAsync("/api/notifications/preferences", Ct)).ShouldBeOkAsync<EmailSettingsDto>();
        settings.EmailEnabled.ShouldBeTrue();
        settings.Items.ShouldAllBe(i => i.Email);
        settings.Items.Single(i => i.Type == NotificationType.Comment).Delivery.ShouldBe(EmailDelivery.Digest);
        var saved = await (await s.Developer.PutAsJsonAsync("/api/notifications/preferences", new { items = new[] { new { type = "Assigned", email = false } } }, Ct))
            .ShouldBeOkAsync<EmailSettingsDto>();
        saved.Items.Single(i => i.Type == NotificationType.Assigned).Email.ShouldBeFalse();

        await AssignAsync(s, third.Key, s.DeveloperUser.Id);
        (await MailsToAsync(s.DeveloperUser.Email)).Count.ShouldBe(2);
        await using (var db = api.CreateDbContext())
        {
            (await db.Notifications.CountAsync(n => n.UserId == s.DeveloperUser.Id && n.Type == NotificationType.Assigned, Ct)).ShouldBe(2);
        }

        (await s.Developer.PutAsJsonAsync("/api/notifications/preferences", new { items = Array.Empty<object>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DailyDigest_CollectsCommentsAndOverdueIssues_Once()
    {
        var s = await SeedAsync();
        var issue = await CreateIssueAsync(s, "Statement export cuts long names", s.DeveloperUser.Id);
        var overdue = await CreateIssueAsync(s, "Ledger total off by one", s.DeveloperUser.Id);
        await CompleteTestingAsync(s);
        await CommentAsync(s.Tester, issue.Key, "Seen again on page 3 & 4");
        await CommentAsync(s.Tester, issue.Key, "Also in the PDF");
        await using (var db = api.CreateDbContext())
        {
            await db.Issues.Where(i => i.Id == overdue.Id).ExecuteUpdateAsync(set => set.SetProperty(i => i.DueDate, DateTimeOffset.UtcNow.AddDays(-2)), Ct);
        }

        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DailyDigestHandler>().HandleAsync(Ct);
        }

        var digest = (await MailsToAsync(s.DeveloperUser.Email)).Where(m => m.Subject.StartsWith("Your QA Tracker summary", StringComparison.Ordinal)).ShouldHaveSingleItem();
        digest.Subject.ShouldBe("Your QA Tracker summary: 2 new comments and 1 overdue issue");
        digest.Body.ShouldContain(overdue.Key);
        digest.Body.ShouldContain("Ledger total off by one");
        digest.Body.ShouldContain($"Test Tester commented on {issue.Key}");

        await using (var db = api.CreateDbContext())
        {
            (await db.JobRuns.SingleAsync(j => j.Name == DailyDigestHandler.JobName, Ct)).LastResult.ShouldNotBeNull();
            (await db.Notifications.CountAsync(n => n.UserId == s.DeveloperUser.Id && n.Type == NotificationType.Comment && n.EmailedAt == null, Ct)).ShouldBe(0);
        }

        // Second run: comments are not repeated; with overdue mails switched off nothing is left to send.
        await s.Developer.PutAsJsonAsync("/api/notifications/preferences", new { items = new[] { new { type = "Overdue", email = false } } }, Ct);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DailyDigestHandler>().HandleAsync(Ct);
        }

        (await MailsToAsync(s.DeveloperUser.Email)).Count(m => m.Subject.StartsWith("Your QA Tracker summary", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public void DigestSlot_IsTheLatest1800Ist()
    {
        var time = new TimeOnly(18, 0);
        DailyDigestHandler.LatestSlot(DateTimeOffset.Parse("2026-10-29T13:00:00Z"), time) // 18:30 IST
            .ShouldBe(DateTimeOffset.Parse("2026-10-29T18:00:00+05:30"));
        DailyDigestHandler.LatestSlot(DateTimeOffset.Parse("2026-10-29T12:00:00Z"), time) // 17:30 IST
            .ShouldBe(DateTimeOffset.Parse("2026-10-28T18:00:00+05:30"));
    }

    private sealed class FakeSender : IEmailSender
    {
        public ConcurrentQueue<string> Sent { get; } = new();

        public string? FailFor { get; init; }

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            if (to == FailFor)
            {
                throw new InvalidOperationException("Mailbox unavailable");
            }

            Sent.Enqueue(to);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Outbox_SendsDueMails_AndRetriesFailuresWithBackoff()
    {
        var ok = $"ok.{Guid.NewGuid():N}@test.local";
        var bad = $"bad.{Guid.NewGuid():N}@test.local";
        await using (var db = api.CreateDbContext())
        {
            db.EmailOutboxes.AddRange(
                new EmailOutbox { ToAddress = ok, Subject = "Hello", Body = "<p>Hi</p>", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 },
                new EmailOutbox { ToAddress = bad, Subject = "Hello", Body = "<p>Hi</p>", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 });
            await db.SaveChangesAsync(Ct);
        }

        var sender = new FakeSender { FailFor = bad };
        await using var scope = api.Services.CreateAsyncScope();
        var processor = new EmailOutboxProcessor(
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
            sender,
            scope.ServiceProvider.GetRequiredService<IClock>(),
            Options.Create(new EmailOptions { Enabled = true, BatchSize = 200 }),
            NullLogger<EmailOutboxProcessor>.Instance);

        // Other tests leave queued mails behind (no worker runs): send until ours have been tried.
        for (var i = 0; i < 20 && ((await MailsToAsync(bad)).Single().Attempts == 0 || (await MailsToAsync(ok)).Single().SentAt is null); i++)
        {
            await processor.SendDueAsync(Ct);
        }

        var delivered = (await MailsToAsync(ok)).Single();
        delivered.SentAt.ShouldNotBeNull();
        delivered.Attempts.ShouldBe(1);
        var failed = (await MailsToAsync(bad)).Single();
        failed.SentAt.ShouldBeNull();
        failed.Attempts.ShouldBe(1);
        failed.LastError.ShouldBe("InvalidOperationException: Mailbox unavailable");
        failed.NextAttemptAt.ShouldNotBeNull();
        failed.NextAttemptAt!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(30));

        // Not due yet: the next run leaves it alone.
        await processor.SendDueAsync(Ct);
        (await MailsToAsync(bad)).Single().Attempts.ShouldBe(1);

        // The admin's retry makes it due again.
        var admin = await api.ClientForAsync(UserRole.Admin);
        (await admin.PostAsync($"/api/admin/email/outbox/{failed.Id}/retry", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reset = (await MailsToAsync(bad)).Single();
        reset.Attempts.ShouldBe(0);
        reset.NextAttemptAt.ShouldBeNull();
        await (await admin.PostAsync($"/api/admin/email/outbox/{delivered.Id}/retry", null, Ct)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "already_sent");
    }

    [Fact]
    public async Task AdminScreen_ShowsSettingsWithoutThePassword_AndReportsSmtpFailures()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var response = await admin.GetAsync("/api/admin/email", Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        raw.ShouldNotContain("password\":", Case.Insensitive);
        var status = JsonSerializer.Deserialize<EmailStatusDto>(raw, ApiFactory.Json)!;
        status.Enabled.ShouldBeTrue();
        status.Smtp.Host.ShouldBe("127.0.0.1");
        status.Smtp.Port.ShouldBe(1);
        status.Smtp.PasswordSet.ShouldBeFalse();
        status.Digest.TimeIst.ShouldBe("18:00");

        var refused = await admin.PostAsJsonAsync("/api/admin/email/test", new { to = "someone@test.local" }, Ct);
        var problem = await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
        problem.Code().ShouldBe("smtp_failed");
        problem.GetProperty("detail").GetString()!.ShouldStartWith("The SMTP server 127.0.0.1:1 did not accept the mail:");

        (await admin.PostAsJsonAsync("/api/admin/email/test", new { to = "not-an-address" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await (await api.ClientForAsync(UserRole.Developer)).GetAsync("/api/admin/email", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PostAsync("/api/admin/email/digest", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>In this class on purpose: tests of one class run one after another, and the screen test above expects the configured server.</summary>
    [Fact]
    public async Task SmtpSettingsSavedInTheApp_AreUsedByTheNextTestMail_AndCanBeReset()
    {
        var admin = await api.ClientForAsync(UserRole.Admin);
        var configured = await (await admin.GetAsync("/api/admin/email/settings", Ct)).ShouldBeOkAsync<MailServerSettingsDto>();
        (configured.SavedInApp, configured.SmtpHost, configured.SmtpPort, configured.RowVersion).ShouldBe((false, "127.0.0.1", 1, (string?)null));

        await using var smtp = new FakeSmtpServer();
        await (await admin.PutAsJsonAsync("/api/admin/email/settings", new { smtpHost = "127.0.0.1", smtpPort = 0, fromAddress = "alerts@test.local", fromName = "QA alerts" }, Ct))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        try
        {
            // First save (no version yet) with a sign-in: the password is stored but never returned.
            var response = await admin.PutAsJsonAsync("/api/admin/email/settings", new
            {
                smtpHost = " relay.test.local ", smtpPort = 587, smtpEnableSsl = true, smtpUserName = "relay", smtpPassword = "s3cret",
                fromAddress = "alerts@test.local", fromName = "QA alerts", rowVersion = (string?)null,
            }, Ct);
            var raw = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
            raw.ShouldNotContain("s3cret");
            var first = JsonSerializer.Deserialize<MailServerSettingsDto>(raw, ApiFactory.Json)!;
            (first.SavedInApp, first.SmtpHost, first.PasswordSet).ShouldBe((true, "relay.test.local", true));

            // Another "first save" is a conflict; with the version it saves – here the fake server without sign-in.
            await (await admin.PutAsJsonAsync("/api/admin/email/settings", new
            {
                smtpHost = "127.0.0.1", smtpPort = smtp.Port, smtpEnableSsl = false, fromAddress = "alerts@test.local", fromName = "QA alerts", rowVersion = (string?)null,
            }, Ct)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency");
            var saved = await (await admin.PutAsJsonAsync("/api/admin/email/settings", new
            {
                smtpHost = "127.0.0.1", smtpPort = smtp.Port, smtpEnableSsl = false, smtpUserName = "", fromAddress = "alerts@test.local", fromName = "QA alerts",
                rowVersion = first.RowVersion,
            }, Ct)).ShouldBeOkAsync<MailServerSettingsDto>();
            (saved.SmtpPort, saved.SmtpUserName, saved.PasswordSet).ShouldBe((smtp.Port, (string?)null, false));

            var sent = await (await admin.PostAsJsonAsync("/api/admin/email/test", new { to = "someone@test.local" }, Ct)).ShouldBeOkAsync<TestEmailResultDto>();
            sent.Server.ShouldBe($"127.0.0.1:{smtp.Port}");
            smtp.Received.ShouldBe([("alerts@test.local", "someone@test.local")]);

            var status = await (await admin.GetAsync("/api/admin/email", Ct)).ShouldBeOkAsync<EmailStatusDto>();
            (status.Smtp.Port, status.From, status.FromName).ShouldBe((smtp.Port, "alerts@test.local", "QA alerts"));
        }
        finally
        {
            var reset = await (await admin.DeleteAsync("/api/admin/email/settings", Ct)).ShouldBeOkAsync<MailServerSettingsDto>();
            (reset.SavedInApp, reset.SmtpPort).ShouldBe((false, 1));
        }

        (await (await api.ClientForAsync(UserRole.Tester)).GetAsync("/api/admin/email/settings", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
