using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Infrastructure.Email;

/// <summary>
/// Sends one batch of due outbox mails (oldest first). A failure is kept on the row (Attempts, LastError) and retried
/// with back-off – 1, 5, 15, 30 min, then hourly – until <see cref="EmailOptions.MaxAttempts"/>; then it stays failed.
/// </summary>
public sealed class EmailOutboxProcessor(
    IApplicationDbContext db,
    IEmailSender sender,
    IClock clock,
    IOptions<EmailOptions> options,
    ILogger<EmailOutboxProcessor> logger)
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
    ];

    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var maxAttempts = options.Value.MaxAttempts;
        var due = await db.EmailOutboxes
            .Where(m => m.SentAt == null && m.Attempts < maxAttempts && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.Id)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var mail in due)
        {
            try
            {
                await sender.SendAsync(mail.ToAddress, mail.Subject, mail.Body, cancellationToken);
                mail.Attempts++;
                mail.SentAt = clock.UtcNow;
                mail.LastError = null;
                mail.NextAttemptAt = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mail.Attempts++;
                mail.LastError = Truncate($"{ex.GetType().Name}: {ex.GetBaseException().Message}", 2000);
                mail.NextAttemptAt = clock.UtcNow + Backoff[Math.Min(mail.Attempts - 1, Backoff.Length - 1)];
                logger.LogWarning("Mail {MailId} to {To} failed (attempt {Attempt}): {Error}", mail.Id, mail.ToAddress, mail.Attempts, mail.LastError);
            }

            // Saved per mail: a crash must not resend the ones already delivered.
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
