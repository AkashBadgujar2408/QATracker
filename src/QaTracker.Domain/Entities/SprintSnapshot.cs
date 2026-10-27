using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class SprintSnapshot
{
    public int SprintId { get; set; }

    public int IssueId { get; set; }

    public IssueStatus StatusAtClose { get; set; }

    public Severity SeverityAtClose { get; set; }

    public int? AssigneeAtCloseId { get; set; }

    public virtual User? AssigneeAtClose { get; set; }

    public virtual Issue Issue { get; set; } = null!;

    public virtual TestingSprint Sprint { get; set; } = null!;
}
