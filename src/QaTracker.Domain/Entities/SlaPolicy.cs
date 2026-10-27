using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class SlaPolicy
{
    public int Id { get; set; }

    public Severity Severity { get; set; }

    public int WorkingHours { get; set; }
}
