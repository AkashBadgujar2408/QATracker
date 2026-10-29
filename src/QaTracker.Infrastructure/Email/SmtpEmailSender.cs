using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Infrastructure.Email;

/// <summary>SMTP delivery with System.Net.Mail (no extra library; fine for an office SMTP relay or smtp4dev).</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var settings = options.Value;
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

        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000,
        };
        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
