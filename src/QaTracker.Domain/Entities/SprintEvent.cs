using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class SprintEvent
{
    public int Id { get; set; }

    public int SprintId { get; set; }

    public int UserId { get; set; }

    public SprintEventKind Kind { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset At { get; set; }

    public virtual TestingSprint Sprint { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
