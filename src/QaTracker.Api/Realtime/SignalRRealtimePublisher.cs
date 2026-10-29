using Microsoft.AspNetCore.SignalR;
using QaTracker.Application.Abstractions;

namespace QaTracker.Api.Realtime;

/// <summary>Sends committed changes to the hub's clients; method names are the event names without "Event".</summary>
internal sealed class SignalRRealtimePublisher(IHubContext<TrackerHub> hub) : IRealtimePublisher
{
    public async Task PublishAsync(IReadOnlyCollection<RealtimeEvent> events, CancellationToken cancellationToken)
    {
        foreach (var e in events)
        {
            var task = e switch
            {
                IssueChangedEvent issue => hub.Clients.All.SendAsync("IssueChanged", issue, cancellationToken),
                ActivityAddedEvent activity => hub.Clients.Group(TrackerHub.IssueGroup(activity.IssueKey)).SendAsync("ActivityAdded", activity, cancellationToken),
                SprintChangedEvent sprint => hub.Clients.All.SendAsync("SprintChanged", sprint, cancellationToken),
                NotificationCreatedEvent created => hub.Clients.Group(TrackerHub.UserGroup(created.UserId)).SendAsync("NotificationCreated", created.Notification, cancellationToken),
                NotificationsReadEvent read => hub.Clients.Group(TrackerHub.UserGroup(read.UserId)).SendAsync("NotificationsRead", new { read.UnreadCount }, cancellationToken),
                _ => Task.CompletedTask,
            };
            await task;
        }
    }
}
