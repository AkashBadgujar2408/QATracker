using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class IssueLink
{
    public int FromIssueId { get; set; }

    public int ToIssueId { get; set; }

    public IssueLinkType Type { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual Issue FromIssue { get; set; } = null!;

    public virtual Issue ToIssue { get; set; } = null!;
}
