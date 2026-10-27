using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class TestingSprint
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string Name { get; set; } = null!;

    public int PhaseId { get; set; }

    public int? DefaultEnvironmentId { get; set; }

    public SprintStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public int StartedById { get; set; }

    public DateTimeOffset? TestingCompletedAt { get; set; }

    public int? TestingCompletedById { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public int? ClosedById { get; set; }

    public string? Notes { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual User? ClosedBy { get; set; }

    public virtual ProjectEnvironment? DefaultEnvironment { get; set; }

    public virtual ICollection<Issue> IssueCarriedFromSprints { get; set; } = new List<Issue>();

    public virtual ICollection<Issue> IssueSprints { get; set; } = new List<Issue>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual SprintPhase Phase { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<SprintEvent> SprintEvents { get; set; } = new List<SprintEvent>();

    public virtual ICollection<SprintSnapshot> SprintSnapshots { get; set; } = new List<SprintSnapshot>();

    public virtual User StartedBy { get; set; } = null!;

    public virtual User? TestingCompletedBy { get; set; }
}
