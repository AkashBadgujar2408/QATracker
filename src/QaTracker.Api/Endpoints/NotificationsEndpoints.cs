using QaTracker.Api.Common;
using QaTracker.Application.Features.Notifications;

namespace QaTracker.Api.Endpoints;

public sealed record MarkNotificationsRequest(IReadOnlyList<int>? Ids, bool? All, bool? Read);

/// <summary>The bell (plan 8.3): the signed-in user's notification history, unread count, mark read / unread.</summary>
public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications");

        group.MapGet("/", async (bool? unread, int? before, int? size, ListNotificationsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListNotificationsQuery(unread ?? false, before, size ?? ListNotificationsQuery.DefaultSize), ct))
                    .ToHttp(page => TypedResults.Ok(page)))
            .WithSummary("Your notifications, newest first (all history, read and unread). Older pages: before = last item's id.");

        group.MapGet("/unread-count", async (UnreadNotificationsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithSummary("Number of unread notifications (the bell badge).");

        group.MapPost("/read", async (MarkNotificationsRequest r, MarkNotificationsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new MarkNotificationsCommand(r.Ids, r.All ?? false, r.Read ?? true), ct)).ToHttp(count => TypedResults.Ok(count)))
            .WithSummary("Mark notifications read (ids or all = true); read = false marks them unread again. Returns the unread count.");

        group.MapGet("/preferences", async (GetNotificationPreferencesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithSummary("Your e-mail choice per notification type (instant or in the daily summary). In-app notifications are always on.");

        group.MapPut("/preferences", async (SavePreferencesCommand command, SaveNotificationPreferencesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(settings => TypedResults.Ok(settings)))
            .WithSummary("Save e-mail choices: items = [{type, email}].");

        return app;
    }
}
