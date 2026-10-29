using QaTracker.Application.Abstractions;

namespace QaTracker.Infrastructure.Persistence;

/// <summary>Default when no SignalR hub is hosted (tools, tests without a host): live updates are dropped.</summary>
internal sealed class NoRealtimePublisher : IRealtimePublisher
{
    public Task PublishAsync(IReadOnlyCollection<RealtimeEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
}
