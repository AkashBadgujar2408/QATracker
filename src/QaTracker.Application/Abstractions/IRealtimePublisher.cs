using QaTracker.Application.Features.Notifications;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Abstractions;

/// <summary>Live updates for open browsers (SignalR hub /hubs/tracker). Sent only after the change is committed.</summary>
public interface IRealtimePublisher
{
    Task PublishAsync(IReadOnlyCollection<RealtimeEvent> events, CancellationToken cancellationToken);
}

/// <summary>Something open screens may want to refresh. Payloads are small: clients reload what they show.</summary>
public abstract record RealtimeEvent;

/// <summary>An issue was created, changed or deleted (lists, board, detail page). Sent to every signed-in user.</summary>
public sealed record IssueChangedEvent(string IssueKey, int ProjectId, int SprintId, bool Deleted) : RealtimeEvent;

/// <summary>A timeline entry was added or edited. Sent to the issue's group (browsers showing that issue).</summary>
public sealed record ActivityAddedEvent(string IssueKey, int ActivityId, ActivityKind Kind) : RealtimeEvent;

/// <summary>A sprint was started or changed state. Sent to every signed-in user.</summary>
public sealed record SprintChangedEvent(int SprintId, int ProjectId, SprintStatus Status) : RealtimeEvent;

/// <summary>A new notification for one user (their bell).</summary>
public sealed record NotificationCreatedEvent(int UserId, NotificationDto Notification) : RealtimeEvent;

/// <summary>The user read notifications in one tab; the other tabs update their bell.</summary>
public sealed record NotificationsReadEvent(int UserId, int UnreadCount) : RealtimeEvent;
