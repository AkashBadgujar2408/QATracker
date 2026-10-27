using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class Attachment
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public int? ActivityId { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string StoragePath { get; set; } = null!;

    public string? ThumbnailPath { get; set; }

    public int UploadedById { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public virtual IssueActivity? Activity { get; set; }

    public virtual Issue Issue { get; set; } = null!;

    public virtual User UploadedBy { get; set; } = null!;
}
