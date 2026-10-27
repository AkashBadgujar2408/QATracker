using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class IssueWatcher
{
    public int IssueId { get; set; }

    public int UserId { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public virtual Issue Issue { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
