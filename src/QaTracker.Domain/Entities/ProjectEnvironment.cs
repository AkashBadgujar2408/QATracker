using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class ProjectEnvironment
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string Name { get; set; } = null!;

    public EnvironmentType Type { get; set; }

    public string Url { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int CreatedById { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual User CreatedBy { get; set; } = null!;

    public virtual ICollection<DeployNotice> DeployNotices { get; set; } = new List<DeployNotice>();

    public virtual ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();

    public virtual ICollection<IssueActivity> IssueActivities { get; set; } = new List<IssueActivity>();

    public virtual ICollection<Issue> Issues { get; set; } = new List<Issue>();

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<TestingSprint> TestingSprints { get; set; } = new List<TestingSprint>();

    public virtual User? UpdatedBy { get; set; }
}
