using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class SprintPhase
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<TestingSprint> TestingSprints { get; set; } = new List<TestingSprint>();
}
