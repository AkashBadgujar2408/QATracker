namespace QaTracker.Application.Abstractions;

/// <summary>Delivers one mail (SMTP). Only the outbox worker calls it; handlers queue mails in EmailOutbox.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken);
}
