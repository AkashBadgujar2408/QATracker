using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public NotificationType Type { get; set; }

    public int? IssueId { get; set; }

    public int? SprintId { get; set; }

    public string Title { get; set; } = null!;

    public string? Body { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EmailedAt { get; set; }

    public virtual Issue? Issue { get; set; }

    public virtual TestingSprint? Sprint { get; set; }

    public virtual User User { get; set; } = null!;
}
