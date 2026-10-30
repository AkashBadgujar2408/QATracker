using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class EmailSetting
{
    public int Id { get; set; }

    public string SmtpHost { get; set; } = null!;

    public int SmtpPort { get; set; }

    public bool SmtpEnableSsl { get; set; }

    public string? SmtpUserName { get; set; }

    public string? SmtpPassword { get; set; }

    public string FromAddress { get; set; } = null!;

    public string FromName { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; }

    public int UpdatedById { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual User UpdatedBy { get; set; } = null!;
}
