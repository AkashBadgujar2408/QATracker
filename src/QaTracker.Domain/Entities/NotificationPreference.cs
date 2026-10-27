using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class NotificationPreference
{
    public int UserId { get; set; }

    public NotificationType Type { get; set; }

    public bool Email { get; set; }

    public virtual User User { get; set; } = null!;
}
