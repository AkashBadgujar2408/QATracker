using QaTracker.Application.Abstractions;

namespace QaTracker.Infrastructure.Time;

internal sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
