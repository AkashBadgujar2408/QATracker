using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Notifications;

public enum EmailDelivery
{
    /// <summary>One mail per notification, right away.</summary>
    Instant,

    /// <summary>Collected into the daily summary (plan 8.3: comments, overdue).</summary>
    Digest,
}

/// <summary>How each notification type is mailed (plan 8.3) and what the preferences page calls it.</summary>
public static class NotificationChannels
{
    public sealed record Channel(NotificationType Type, EmailDelivery Delivery, string Label, string Description);

    public static readonly IReadOnlyList<Channel> All =
    [
        new(NotificationType.Assigned, EmailDelivery.Instant, "Issue assigned to you", "Someone assigns or reassigns an issue to you."),
        new(NotificationType.Mentioned, EmailDelivery.Instant, "Mentions", "Someone writes @your.name in a comment."),
        new(NotificationType.Reopened, EmailDelivery.Instant, "Reopened", "An issue you work on fails its retest or is reopened."),
        new(NotificationType.Fixed, EmailDelivery.Instant, "Ready to retest", "A fix for an issue you reported is deployed (one mail per deploy notice)."),
        new(NotificationType.NeedInfo, EmailDelivery.Instant, "Needs information", "A developer asks a question on an issue you reported."),
        new(NotificationType.Disputed, EmailDelivery.Instant, "Disputed", "A developer disputes an issue you reported."),
        new(NotificationType.TestingCompleted, EmailDelivery.Instant, "Testing completed", "Testing of a sprint in your project is completed (one summary)."),
        new(NotificationType.CriticalUnassigned, EmailDelivery.Instant, "Critical issue unassigned", "A critical issue waits more than 2 hours without a developer."),
        new(NotificationType.Comment, EmailDelivery.Digest, "Comments", "New comments on issues you report, work on or watch."),
        new(NotificationType.Overdue, EmailDelivery.Digest, "Overdue issues", "Issues assigned to you that passed their due date."),
    ];

    private static readonly Dictionary<NotificationType, Channel> ByType = All.ToDictionary(c => c.Type);

    public static Channel Of(NotificationType type) => ByType[type];

    /// <summary>Without a saved preference every type is mailed (plan 8.3 defaults).</summary>
    public const bool DefaultEmail = true;
}

/// <summary>
/// Puts instant notification mails into the outbox in the same save as the notifications (ADR-029). Respects the
/// user's preferences; digest types wait for <see cref="DailyDigestHandler"/>.
/// </summary>
public sealed class NotificationMailer(IOptions<EmailOptions> options, IClock clock)
{
    public bool Enabled => options.Value.Enabled;

    public async Task QueueAsync(IApplicationDbContext db, IReadOnlyList<(Notification Notification, string? IssueKey, int? ProjectId)> created, CancellationToken cancellationToken)
    {
        var instant = created.Where(c => NotificationChannels.Of(c.Notification.Type).Delivery == EmailDelivery.Instant).ToList();
        if (!Enabled || instant.Count == 0)
        {
            return;
        }

        var userIds = instant.Select(c => c.Notification.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .ToDictionaryAsync(u => u.Id, u => new { u.Email, u.FullName }, cancellationToken);
        var optedOut = (await db.NotificationPreferences.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId) && !p.Email)
                .Select(p => new { p.UserId, p.Type })
                .ToListAsync(cancellationToken))
            .Select(p => (p.UserId, p.Type))
            .ToHashSet();

        var now = clock.UtcNow;
        foreach (var (notification, issueKey, projectId) in instant)
        {
            if (!users.TryGetValue(notification.UserId, out var user) || string.IsNullOrWhiteSpace(user.Email) || optedOut.Contains((notification.UserId, notification.Type)))
            {
                continue;
            }

            var link = EmailTemplates.Link(options.Value.AppBaseUrl, issueKey, projectId, notification.SprintId);
            var (subject, html) = EmailTemplates.Notification(options.Value, user.FullName, notification, issueKey, link);
            db.EmailOutboxes.Add(new EmailOutbox { ToAddress = user.Email, Subject = subject, Body = html, CreatedAt = now, Attempts = 0 });
            notification.EmailedAt = now;
        }
    }
}

/// <summary>
/// Daily summary at 18:00 IST (ADR-019/029): per user the comment notifications not mailed yet (grouped by issue) and
/// the issues assigned to them that are overdue – each part only if the user wants it. Comment notifications are marked
/// mailed even when the user opted out, so turning the mail on later does not send old ones.
/// </summary>
public sealed class DailyDigestHandler(IApplicationDbContext db, IOptions<EmailOptions> options, IClock clock)
{
    public const string JobName = "daily-digest";

    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var comments = await db.Notifications
            .Where(n => n.Type == NotificationType.Comment && n.EmailedAt == null && n.CreatedAt <= now)
            .Include(n => n.Issue)
            .OrderBy(n => n.Id)
            .ToListAsync(cancellationToken);

        var awaitingFix = IssueStates.AwaitingFix.ToList();
        var overdue = await db.Issues.AsNoTracking()
            .Where(i => i.AssigneeId != null && i.DueDate != null && i.DueDate < now && awaitingFix.Contains(i.Status) && i.Sprint.Status == SprintStatus.TestingCompleted)
            .OrderBy(i => i.DueDate)
            .Select(i => new DigestIssue(i.AssigneeId!.Value, i.IssueKey, i.Title, i.Severity, i.DueDate))
            .ToListAsync(cancellationToken);

        var userIds = comments.Select(c => c.UserId).Concat(overdue.Select(o => o.UserId)).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsActive)
            .ToDictionaryAsync(u => u.Id, u => new { u.Email, u.FullName }, cancellationToken);
        var optedOut = (await db.NotificationPreferences.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId) && !p.Email && (p.Type == NotificationType.Comment || p.Type == NotificationType.Overdue))
                .Select(p => new { p.UserId, p.Type })
                .ToListAsync(cancellationToken))
            .Select(p => (p.UserId, p.Type))
            .ToHashSet();

        var sent = 0;
        foreach (var userId in userIds)
        {
            if (!users.TryGetValue(userId, out var user) || string.IsNullOrWhiteSpace(user.Email))
            {
                continue;
            }

            var myComments = optedOut.Contains((userId, NotificationType.Comment)) ? [] : comments.Where(c => c.UserId == userId).ToList();
            var myOverdue = optedOut.Contains((userId, NotificationType.Overdue)) ? [] : overdue.Where(o => o.UserId == userId).ToList();
            if (myComments.Count == 0 && myOverdue.Count == 0)
            {
                continue;
            }

            var (subject, html) = EmailTemplates.Digest(options.Value, user.FullName, myComments, myOverdue, now);
            db.EmailOutboxes.Add(new EmailOutbox { ToAddress = user.Email, Subject = subject, Body = html, CreatedAt = now, Attempts = 0 });
            sent++;
        }

        foreach (var comment in comments)
        {
            comment.EmailedAt = now;
        }

        var run = await db.JobRuns.SingleOrDefaultAsync(j => j.Name == JobName, cancellationToken);
        if (run is null)
        {
            run = new JobRun { Name = JobName };
            db.JobRuns.Add(run);
        }

        run.LastRunAt = now;
        run.LastResult = string.Create(CultureInfo.InvariantCulture, $"{sent} digest mails, {comments.Count} comments, {overdue.Count} overdue issues");
        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }

    /// <summary>When the digest last ran (null: never) – the worker catches up a missed slot after a restart.</summary>
    public async Task<DateTimeOffset?> LastRunAsync(CancellationToken cancellationToken) =>
        await db.JobRuns.AsNoTracking().Where(j => j.Name == JobName).Select(j => (DateTimeOffset?)j.LastRunAt).SingleOrDefaultAsync(cancellationToken);

    /// <summary>The most recent digest slot at or before <paramref name="now"/> (IST wall time, every day).</summary>
    public static DateTimeOffset LatestSlot(DateTimeOffset now, TimeOnly time)
    {
        var ist = now.ToOffset(WorkingCalendar.Ist);
        var today = new DateTimeOffset(DateOnly.FromDateTime(ist.DateTime).ToDateTime(time), WorkingCalendar.Ist);
        return today <= ist ? today : today.AddDays(-1);
    }
}

public sealed record DigestIssue(int UserId, string Key, string Title, Severity Severity, DateTimeOffset? DueDate);

/// <summary>Simple branded HTML mails (inline styles for mail clients). Everything user-written is HTML-encoded.</summary>
public static class EmailTemplates
{
    private const string Brand = "#4f46e5";
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";

    public static string? Link(string baseUrl, string? issueKey, int? projectId, int? sprintId)
    {
        var root = baseUrl.TrimEnd('/');
        if (issueKey is not null)
        {
            return $"{root}/issues/{Uri.EscapeDataString(issueKey)}";
        }

        return projectId is { } project
            ? string.Create(CultureInfo.InvariantCulture, $"{root}/issues?project={project}{(sprintId is { } s ? $"&sprint={s}" : string.Empty)}")
            : null;
    }

    public static (string Subject, string Html) Notification(EmailOptions options, string recipient, Notification notification, string? issueKey, string? link)
    {
        var channel = NotificationChannels.Of(notification.Type);
        var subject = notification.Title;
        var body = new StringBuilder();
        body.Append(Paragraph($"Hi {E(FirstName(recipient))},", Ink));
        body.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 6px;font-size:18px;font-weight:600;color:{Ink};line-height:1.35\">{E(notification.Title)}</p>");
        if (!string.IsNullOrWhiteSpace(notification.Body))
        {
            body.Append(Paragraph(E(notification.Body), Muted));
        }

        if (link is not null)
        {
            body.Append(Button(link, issueKey is not null ? $"Open {issueKey}" : "Open the issues"));
        }

        return (subject, Layout(options, channel.Label, body.ToString(), $"You get this mail for “{channel.Label}”."));
    }

    public static (string Subject, string Html) Digest(EmailOptions options, string recipient, IReadOnlyList<Notification> comments, IReadOnlyList<DigestIssue> overdue, DateTimeOffset now)
    {
        var parts = new List<string>();
        if (comments.Count > 0)
        {
            parts.Add(comments.Count == 1 ? "1 new comment" : $"{comments.Count} new comments");
        }

        if (overdue.Count > 0)
        {
            parts.Add(overdue.Count == 1 ? "1 overdue issue" : $"{overdue.Count} overdue issues");
        }

        var subject = $"Your QA Tracker summary: {string.Join(" and ", parts)}";
        var root = options.AppBaseUrl.TrimEnd('/');
        var body = new StringBuilder();
        body.Append(Paragraph($"Hi {E(FirstName(recipient))}, here is what happened since the last summary.", Ink));

        if (overdue.Count > 0)
        {
            body.Append(Heading("Overdue – assigned to you"));
            foreach (var issue in overdue)
            {
                var due = issue.DueDate is { } d ? $"due {FormatIst(d)}" : string.Empty;
                body.Append(Row($"{root}/issues/{Uri.EscapeDataString(issue.Key)}", issue.Key, issue.Title, $"{issue.Severity} · {due}"));
            }
        }

        if (comments.Count > 0)
        {
            body.Append(Heading("New comments"));
            foreach (var group in comments.GroupBy(c => c.Issue?.IssueKey ?? string.Empty))
            {
                var first = group.First();
                var title = first.Issue?.Title ?? string.Empty;
                var lines = string.Join("<br>", group.Select(c => E(c.Title)));
                body.Append(group.Key.Length == 0
                    ? Paragraph(lines, Muted)
                    : Row($"{root}/issues/{Uri.EscapeDataString(group.Key)}", group.Key, title, null, lines));
            }
        }

        body.Append(Button($"{root}/notifications", "See all notifications"));
        return (subject, Layout(options, $"Daily summary · {FormatIst(now)}", body.ToString(), "You get this summary for comments and overdue issues."));
    }

    public static (string Subject, string Html) Test(EmailOptions options, EmailAdmin.SmtpConfiguration smtp, string admin, DateTimeOffset now)
    {
        var body = Paragraph($"This is a test mail sent by {E(admin)} from the QA Tracker e-mail settings on {FormatIst(now)}.", Ink)
            + Paragraph(E($"Sender: {smtp.FromName} <{smtp.From}>, SMTP server {smtp.Server}."), Muted)
            + Paragraph("If you can read this, notification mails will arrive.", Muted);
        return ("QA Tracker test mail", Layout(options, "Test mail", body, "An administrator sent this test mail."));
    }

    private static string Layout(EmailOptions options, string kicker, string content, string reason)
    {
        var settings = $"{options.AppBaseUrl.TrimEnd('/')}/settings/notifications";
        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>QA Tracker</title></head>
            <body style="margin:0;padding:0;background:#f8fafc;font-family:'Segoe UI',Inter,Arial,sans-serif;font-size:15px;color:{Ink}">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f8fafc;padding:24px 12px">
            <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px">
            <tr><td style="padding:0 4px 14px">
              <span style="display:inline-block;width:26px;height:26px;border-radius:7px;background:{Brand};color:#fff;font-weight:700;font-size:15px;line-height:26px;text-align:center;vertical-align:middle">Q</span>
              <span style="font-weight:700;font-size:16px;color:{Ink};vertical-align:middle;margin-left:6px">QA Tracker</span>
              <span style="font-size:13px;color:{Muted};vertical-align:middle;margin-left:8px">{E(kicker)}</span>
            </td></tr>
            <tr><td style="background:#ffffff;border:1px solid {Line};border-radius:10px;padding:24px">
            {content}
            </td></tr>
            <tr><td style="padding:14px 4px;font-size:13px;color:{Muted};line-height:1.5">
              {E(reason)} <a href="{E(settings)}" style="color:{Brand}">Change e-mail settings</a>.
            </td></tr>
            </table>
            </td></tr></table>
            </body></html>
            """;
    }

    private static string Paragraph(string html, string color) =>
        $"<p style=\"margin:0 0 14px;line-height:1.5;color:{color}\">{html}</p>";

    private static string Heading(string text) =>
        $"<p style=\"margin:20px 0 8px;font-size:13px;font-weight:600;color:{Muted}\">{E(text)}</p>";

    private static string Button(string href, string label) =>
        $"<p style=\"margin:20px 0 0\"><a href=\"{E(href)}\" style=\"display:inline-block;background:{Brand};color:#ffffff;text-decoration:none;font-weight:600;padding:10px 18px;border-radius:6px\">{E(label)}</a></p>";

    private static string Row(string href, string key, string title, string? meta, string? extraHtml = null) =>
        $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-top:1px solid {Line}"><tr><td style="padding:10px 0;line-height:1.45">
          <a href="{E(href)}" style="color:{Brand};font-weight:600;text-decoration:none">{E(key)}</a>
          <span style="color:{Ink}"> {E(title)}</span>
          {(meta is null ? string.Empty : $"<br><span style=\"font-size:13px;color:{Muted}\">{E(meta)}</span>")}
          {(extraHtml is null ? string.Empty : $"<br><span style=\"font-size:13px;color:{Muted}\">{extraHtml}</span>")}
        </td></tr></table>
        """;

    private static string FirstName(string fullName) => fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;

    /// <summary>27/Oct/2026 in IST (ADR-014).</summary>
    public static string FormatIst(DateTimeOffset value) =>
        value.ToOffset(WorkingCalendar.Ist).ToString("dd'/'MMM'/'yyyy", CultureInfo.InvariantCulture);

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
