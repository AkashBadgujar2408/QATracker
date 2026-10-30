using System.Diagnostics;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Notifications;

namespace QaTracker.Application.Features.EmailAdmin;

public sealed record SmtpSettingsDto(string Host, int Port, bool EnableSsl, string? UserName, bool PasswordSet);

public sealed record OutboxStatsDto(int Pending, int Failed, int SentLast24Hours, DateTimeOffset? OldestPendingAt, DateTimeOffset? LastSentAt);

public sealed record OutboxMailDto(int Id, string To, string Subject, int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? NextAttemptAt, DateTimeOffset? SentAt);

public sealed record DigestStatusDto(string TimeIst, DateTimeOffset? LastRunAt, string? LastResult);

/// <summary>What the admin e-mail screen shows. The SMTP password is never returned – only whether one is set.</summary>
public sealed record EmailStatusDto(
    bool Enabled,
    bool Workers,
    string From,
    string FromName,
    string AppBaseUrl,
    int MaxAttempts,
    SmtpSettingsDto Smtp,
    OutboxStatsDto Outbox,
    IReadOnlyList<OutboxMailDto> Problems,
    IReadOnlyList<OutboxMailDto> Recent,
    DigestStatusDto Digest);

public sealed record SendTestEmailCommand(string? To);

public sealed record TestEmailResultDto(string To, long DurationMs, string Server);

public sealed record DigestRunDto(int Queued);

public sealed class SendTestEmailValidator : AbstractValidator<SendTestEmailCommand>
{
    public SendTestEmailValidator()
    {
        RuleFor(x => x.To).NotEmpty().WithMessage("Enter the address to send the test mail to.")
            .EmailAddress().WithMessage("Enter a valid e-mail address.")
            .MaximumLength(256);
    }
}

public sealed class EmailStatusHandler(IApplicationDbContext db, EmailSettingsSource settings, IOptions<EmailOptions> options, IClock clock)
{
    private const int ListSize = 10;

    public async Task<EmailStatusDto> HandleAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var dayAgo = clock.UtcNow.AddDays(-1);
        var outbox = db.EmailOutboxes.AsNoTracking();
        var waiting = outbox.Where(m => m.SentAt == null && m.Attempts < o.MaxAttempts);

        var pending = await waiting.CountAsync(cancellationToken);
        var failed = await outbox.CountAsync(m => m.SentAt == null && m.Attempts >= o.MaxAttempts, cancellationToken);
        var sent = await outbox.CountAsync(m => m.SentAt != null && m.SentAt >= dayAgo, cancellationToken);
        var oldest = await waiting.MinAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);
        var lastSent = await outbox.MaxAsync(m => m.SentAt, cancellationToken);

        var problems = await outbox.Where(m => m.SentAt == null && m.LastError != null).OrderByDescending(m => m.Id).Take(ListSize)
            .Select(m => new OutboxMailDto(m.Id, m.ToAddress, m.Subject, m.Attempts, m.LastError, m.CreatedAt, m.NextAttemptAt, m.SentAt))
            .ToListAsync(cancellationToken);
        var recent = await outbox.OrderByDescending(m => m.Id).Take(ListSize)
            .Select(m => new OutboxMailDto(m.Id, m.ToAddress, m.Subject, m.Attempts, m.LastError, m.CreatedAt, m.NextAttemptAt, m.SentAt))
            .ToListAsync(cancellationToken);
        var digest = await db.JobRuns.AsNoTracking().Where(j => j.Name == DailyDigestHandler.JobName)
            .Select(j => new { j.LastRunAt, j.LastResult })
            .SingleOrDefaultAsync(cancellationToken);
        var smtp = await settings.GetAsync(cancellationToken);

        return new EmailStatusDto(
            o.Enabled,
            o.Workers,
            smtp.From,
            smtp.FromName,
            o.AppBaseUrl,
            o.MaxAttempts,
            new SmtpSettingsDto(smtp.Host, smtp.Port, smtp.EnableSsl, smtp.UserName, !string.IsNullOrEmpty(smtp.Password)),
            new OutboxStatsDto(pending, failed, sent, oldest, lastSent),
            problems,
            recent,
            new DigestStatusDto(o.DigestTimeIst, digest?.LastRunAt, digest?.LastResult));
    }
}

/// <summary>
/// Sends a test mail straight through SMTP (not the outbox) so the admin sees the server's answer at once – also while
/// e-mail is switched off, to check the settings before switching it on.
/// </summary>
public sealed class SendTestEmailHandler(
    IValidator<SendTestEmailCommand> validator,
    IEmailSender sender,
    EmailSettingsSource settings,
    IOptions<EmailOptions> options,
    ICurrentUser currentUser,
    IApplicationDbContext db,
    IClock clock)
{
    public async Task<Result<TestEmailResultDto>> HandleAsync(SendTestEmailCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var smtp = await settings.GetAsync(cancellationToken);
        var to = command.To!.Trim();
        var admin = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.FullName).SingleAsync(cancellationToken);
        var (subject, html) = EmailTemplates.Test(options.Value, smtp, admin, clock.UtcNow);
        var server = smtp.Server;
        var watch = Stopwatch.StartNew();
        try
        {
            await sender.SendAsync(to, subject, html, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.Rule("smtp_failed", $"The SMTP server {server} did not accept the mail: {ex.GetBaseException().Message}");
        }

        return new TestEmailResultDto(to, watch.ElapsedMilliseconds, server);
    }
}

/// <summary>Queue the daily summary now (records the run, so the 18:00 slot does not send it again).</summary>
public sealed class RunDigestNowHandler(DailyDigestHandler digest, IOptions<EmailOptions> options)
{
    public async Task<Result<DigestRunDto>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Error.Rule("email_disabled", "E-mail is switched off (Email:Enabled) – nothing would be sent.");
        }

        return new DigestRunDto(await digest.HandleAsync(cancellationToken));
    }
}

/// <summary>Give an unsent mail a fresh set of attempts. Mails are never deleted (ADR-019: no delete in the jobs admin).</summary>
public sealed class RetryEmailHandler(IApplicationDbContext db)
{
    public async Task<Result> HandleAsync(int id, CancellationToken cancellationToken)
    {
        var mail = await db.EmailOutboxes.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mail is null)
        {
            return Error.NotFound("Mail");
        }

        if (mail.SentAt is not null)
        {
            return Error.Rule("already_sent", "This mail was already sent.");
        }

        mail.Attempts = 0;
        mail.NextAttemptAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
