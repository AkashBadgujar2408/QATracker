using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class SavedView
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Name { get; set; } = null!;

    public string Page { get; set; } = null!;

    public string FilterJson { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
