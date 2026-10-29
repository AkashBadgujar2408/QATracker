using System.ComponentModel.DataAnnotations;

namespace QaTracker.Application.Common;

/// <summary>E-mail notifications (ADR-029), section <c>Email</c>. The SMTP password belongs in user-secrets.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Queue e-mails at all. Off: in-app notifications only, nothing goes into the outbox.</summary>
    public bool Enabled { get; set; }

    /// <summary>Run the background sender and the daily digest in this process (off in integration tests).</summary>
    public bool Workers { get; set; } = true;

    [Required, EmailAddress]
    public string From { get; set; } = "qa-tracker@localhost";

    [Required, MaxLength(100)]
    public string FromName { get; set; } = "QA Tracker";

    /// <summary>Address of the Angular app, for links in mails.</summary>
    [Required, Url]
    public string AppBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Daily digest time in IST, "HH:mm" (Akash: 18:00).</summary>
    [Required, RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    public string DigestTimeIst { get; set; } = "18:00";

    [Range(1, 3600)]
    public int SendIntervalSeconds { get; set; } = 15;

    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 8;

    [Range(1, 200)]
    public int BatchSize { get; set; } = 20;

    public SmtpSettings Smtp { get; set; } = new();

    public TimeOnly DigestTime => TimeOnly.ParseExact(DigestTimeIst, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public sealed class SmtpSettings
    {
        [Required]
        public string Host { get; set; } = "localhost";

        [Range(1, 65535)]
        public int Port { get; set; } = 25;

        public bool EnableSsl { get; set; }

        public string? UserName { get; set; }

        public string? Password { get; set; }
    }
}
