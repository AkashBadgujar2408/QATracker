using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class DeployNotice
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public int EnvironmentId { get; set; }

    public int CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? Note { get; set; }

    public virtual User CreatedBy { get; set; } = null!;

    public virtual ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();

    public virtual ProjectEnvironment Environment { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;
}
