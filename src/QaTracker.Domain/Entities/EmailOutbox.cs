using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class EmailOutbox
{
    public int Id { get; set; }

    public string ToAddress { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public string Body { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
