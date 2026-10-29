using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class JobRun
{
    public string Name { get; set; } = null!;

    public DateTimeOffset LastRunAt { get; set; }

    public string? LastResult { get; set; }
}
