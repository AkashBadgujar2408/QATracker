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

public sealed record NotificationPreferenceDto(NotificationType Type, string Label, string Description, EmailDelivery Delivery, bool Email);

public sealed record EmailSettingsDto(bool EmailEnabled, string DigestTimeIst, string? Email, IReadOnlyList<NotificationPreferenceDto> Items);

public sealed record SavePreferenceItem(NotificationType Type, bool Email);

public sealed record SavePreferencesCommand(IReadOnlyList<SavePreferenceItem>? Items);

/// <summary>E-mail choice per notification type (in-app notifications are always on). No saved row = mailed.</summary>
public sealed class GetNotificationPreferencesHandler(IApplicationDbContext db, ICurrentUser currentUser, Microsoft.Extensions.Options.IOptions<EmailOptions> options)
{
    public async Task<EmailSettingsDto> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var saved = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId).ToDictionaryAsync(p => p.Type, p => p.Email, cancellationToken);
        var email = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(cancellationToken);
        var items = NotificationChannels.All
            .Select(c => new NotificationPreferenceDto(c.Type, c.Label, c.Description, c.Delivery, saved.GetValueOrDefault(c.Type, NotificationChannels.DefaultEmail)))
            .ToList();
        return new EmailSettingsDto(options.Value.Enabled, options.Value.DigestTimeIst, string.IsNullOrWhiteSpace(email) ? null : email, items);
    }
}

public sealed class SaveNotificationPreferencesHandler(IApplicationDbContext db, ICurrentUser currentUser, GetNotificationPreferencesHandler reader)
{
    public async Task<Result<EmailSettingsDto>> HandleAsync(SavePreferencesCommand command, CancellationToken cancellationToken)
    {
        if (command.Items is null || command.Items.Count == 0)
        {
            return Error.Validation("items", "Send at least one preference.");
        }

        if (command.Items.Any(i => !Enum.IsDefined(i.Type)) || command.Items.Select(i => i.Type).Distinct().Count() != command.Items.Count)
        {
            return Error.Validation("items", "Each notification type may appear once.");
        }

        var userId = currentUser.UserId;
        var existing = await db.NotificationPreferences.Where(p => p.UserId == userId).ToDictionaryAsync(p => p.Type, cancellationToken);
        foreach (var item in command.Items)
        {
            if (existing.TryGetValue(item.Type, out var row))
            {
                row.Email = item.Email;
            }
            else
            {
                db.NotificationPreferences.Add(new NotificationPreference { UserId = userId, Type = item.Type, Email = item.Email });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await reader.HandleAsync(cancellationToken);
    }
}
