using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class Holiday
{
    public DateOnly Date { get; set; }

    public string Name { get; set; } = null!;
}
