using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class IssueActivity
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public int? UserId { get; set; }

    public ActivityKind Kind { get; set; }

    public string? Body { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public int? EnvironmentId { get; set; }

    public int? DeploymentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    public virtual Deployment? Deployment { get; set; }

    public virtual ProjectEnvironment? Environment { get; set; }

    public virtual Issue Issue { get; set; } = null!;

    public virtual ICollection<Mention> Mentions { get; set; } = new List<Mention>();

    public virtual User? User { get; set; }
}
