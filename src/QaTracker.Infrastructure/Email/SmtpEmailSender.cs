using System.Net;
using System.Net.Mail;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Features.EmailAdmin;

namespace QaTracker.Infrastructure.Email;

/// <summary>
/// SMTP delivery with System.Net.Mail (no extra library; fine for an office SMTP relay or smtp4dev). Uses the settings
/// saved on Admin › E-mail, otherwise the configuration (ADR-038).
/// </summary>
internal sealed class SmtpEmailSender(EmailSettingsSource settingsSource) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var settings = await settingsSource.GetAsync(cancellationToken);
        using var message = new MailMessage
        {
            From = new MailAddress(settings.From, settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };
        message.To.Add(new MailAddress(to));

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000,
        };
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
