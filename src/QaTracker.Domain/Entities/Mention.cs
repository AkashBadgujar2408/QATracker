using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class Mention
{
    public int Id { get; set; }

    public int ActivityId { get; set; }

    public int UserId { get; set; }

    public virtual IssueActivity Activity { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
