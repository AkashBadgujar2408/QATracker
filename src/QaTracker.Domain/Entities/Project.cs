using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class Project
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public string Name { get; set; } = null!;

    public string ProjectKey { get; set; } = null!;

    public int NextIssueNumber { get; set; }

    public bool RequireLiveVerification { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int CreatedById { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Client Client { get; set; } = null!;

    public virtual User CreatedBy { get; set; } = null!;

    public virtual ICollection<DeployNotice> DeployNotices { get; set; } = new List<DeployNotice>();

    public virtual ICollection<Issue> Issues { get; set; } = new List<Issue>();

    public virtual ICollection<Module> Modules { get; set; } = new List<Module>();

    public virtual ICollection<ProjectEnvironment> ProjectEnvironments { get; set; } = new List<ProjectEnvironment>();

    public virtual ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();

    public virtual TestingSprint? TestingSprint { get; set; }

    public virtual User? UpdatedBy { get; set; }
}
