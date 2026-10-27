using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class Module
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int CreatedById { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual User CreatedBy { get; set; } = null!;

    public virtual ICollection<Issue> Issues { get; set; } = new List<Issue>();

    public virtual Project Project { get; set; } = null!;

    public virtual User? UpdatedBy { get; set; }
}
