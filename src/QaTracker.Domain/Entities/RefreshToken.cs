using System;
using System.Collections.Generic;

namespace QaTracker.Domain.Entities;

public partial class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsPersistent { get; set; }

    public virtual User User { get; set; } = null!;
}
