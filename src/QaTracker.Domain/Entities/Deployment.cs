using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class Deployment
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public int EnvironmentId { get; set; }

    public int? DeployNoticeId { get; set; }

    public int DeployedById { get; set; }

    public DateTimeOffset DeployedAt { get; set; }

    public string? Note { get; set; }

    public string? Version { get; set; }

    public RetestResult RetestResult { get; set; }

    public int? RetestedById { get; set; }

    public DateTimeOffset? RetestedAt { get; set; }

    public string? RetestComment { get; set; }

    public virtual DeployNotice? DeployNotice { get; set; }

    public virtual User DeployedBy { get; set; } = null!;

    public virtual ProjectEnvironment Environment { get; set; } = null!;

    public virtual Issue Issue { get; set; } = null!;

    public virtual ICollection<IssueActivity> IssueActivities { get; set; } = new List<IssueActivity>();

    public virtual User? RetestedBy { get; set; }
}
