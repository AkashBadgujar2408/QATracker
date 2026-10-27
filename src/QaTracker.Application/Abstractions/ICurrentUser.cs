using QaTracker.Domain.Enums;

namespace QaTracker.Application.Abstractions;

/// <summary>The signed-in user of the current request (from the access token).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>User id; throws when not authenticated.</summary>
    int UserId { get; }

    UserRole Role { get; }

    string? IpAddress { get; }
}
