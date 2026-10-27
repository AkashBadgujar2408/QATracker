using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class Client
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? ContactPerson { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int CreatedById { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual User CreatedBy { get; set; } = null!;

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    public virtual User? UpdatedBy { get; set; }
}
