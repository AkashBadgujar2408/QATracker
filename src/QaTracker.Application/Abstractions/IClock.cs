namespace QaTracker.Application.Abstractions;

/// <summary>Current time source. Always UTC; inject instead of using DateTimeOffset.UtcNow so code stays testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
