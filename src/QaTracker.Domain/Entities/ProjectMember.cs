using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class ProjectMember
{
    public int ProjectId { get; set; }

    public int UserId { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
