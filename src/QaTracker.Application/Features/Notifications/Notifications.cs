using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Notifications;

public sealed record NotificationDto(
    int Id,
    NotificationType Type,
    string Title,
    string? Body,
    string? IssueKey,
    int? SprintId,
    int? ProjectId,
    bool IsRead,
    DateTimeOffset CreatedAt)
{
    public static readonly System.Linq.Expressions.Expression<Func<Notification, NotificationDto>> Projection = n => new NotificationDto(
        n.Id,
        n.Type,
        n.Title,
        n.Body,
        n.Issue != null ? n.Issue.IssueKey : null,
        n.SprintId,
        n.Issue != null ? n.Issue.ProjectId : n.Sprint != null ? n.Sprint.ProjectId : null,
        n.IsRead,
        n.CreatedAt);
}

public sealed record NotificationPage(IReadOnlyList<NotificationDto> Items, int UnreadCount, bool HasMore);

/// <summary>History of the current user's notifications, newest first. Older pages: <c>before</c> = the last item's id.</summary>
public sealed record ListNotificationsQuery(bool UnreadOnly = false, int? Before = null, int Size = ListNotificationsQuery.DefaultSize)
{
    public const int DefaultSize = 20;
    public const int MaxSize = 100;
}

/// <summary>Mark the given notifications (or all) read – or unread again with <c>Read = false</c>.</summary>
public sealed record MarkNotificationsCommand(IReadOnlyList<int>? Ids, bool All, bool Read = true);

public sealed record UnreadCountDto(int UnreadCount);

public sealed class ListNotificationsHandler(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<NotificationPage>> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (query.Size is < 1 or > ListNotificationsQuery.MaxSize)
        {
            return Error.Validation("size", $"Size must be between 1 and {ListNotificationsQuery.MaxSize}.");
        }

        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == currentUser.UserId);
        var page = mine;
        if (query.UnreadOnly)
        {
            page = page.Where(n => !n.IsRead);
        }

        if (query.Before is { } before)
        {
            page = page.Where(n => n.Id < before);
        }

        var items = await page.OrderByDescending(n => n.Id).Take(query.Size + 1).Select(NotificationDto.Projection).ToListAsync(cancellationToken);
        var unread = await mine.CountAsync(n => !n.IsRead, cancellationToken);
        return new NotificationPage(items.Take(query.Size).ToList(), unread, items.Count > query.Size);
    }
}

public sealed class UnreadNotificationsHandler(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<UnreadCountDto> HandleAsync(CancellationToken cancellationToken) =>
        new(await db.Notifications.CountAsync(n => n.UserId == currentUser.UserId && !n.IsRead, cancellationToken));
}

/// <summary>Only the user's own notifications are touched (other ids are ignored). Other open tabs get the new count.</summary>
public sealed class MarkNotificationsHandler(IApplicationDbContext db, ICurrentUser currentUser, IRealtimePublisher realtime)
{
    public async Task<Result<UnreadCountDto>> HandleAsync(MarkNotificationsCommand command, CancellationToken cancellationToken)
    {
        if (!command.All && (command.Ids is null || command.Ids.Count == 0))
        {
            return Error.Validation("ids", "Choose the notifications to mark.");
        }

        if (command.Ids is { Count: > ListNotificationsQuery.MaxSize * 10 })
        {
            return Error.Validation("ids", "Too many notifications at once.");
        }

        var userId = currentUser.UserId;
        var target = db.Notifications.Where(n => n.UserId == userId && n.IsRead != command.Read);
        if (!command.All)
        {
            var ids = command.Ids!.Distinct().ToList();
            target = target.Where(n => ids.Contains(n.Id));
        }

        var changed = await target.ExecuteUpdateAsync(set => set.SetProperty(n => n.IsRead, command.Read), cancellationToken);
        var unread = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
        if (changed > 0)
        {
            await realtime.PublishAsync([new NotificationsReadEvent(userId, unread)], cancellationToken);
        }

        return new UnreadCountDto(unread);
    }
}
