using Microsoft.AspNetCore.Mvc;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Timeline;

namespace QaTracker.Api.Endpoints;

public sealed record EditCommentRequest(string? Body);

/// <summary>
/// Issue timeline (plan 7.5, 14): activities (oldest first, paged backwards), comments for every role in every sprint
/// state, and related open issues on the same screen/module.
/// </summary>
public static class TimelineEndpoints
{
    public static IEndpointRouteBuilder MapTimelineEndpoints(this IEndpointRouteBuilder app)
    {
        var issues = app.MapGroup("/api/issues/{key}").WithTags("Timeline");

        issues.MapGet("/activities", async (string key, int? before, int? size, TimelineFilter? filter, ListActivitiesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListActivitiesQuery(key, before, size ?? ListActivitiesQuery.DefaultSize, filter ?? TimelineFilter.All), ct))
                    .ToHttp(page => TypedResults.Ok(page)))
            .WithSummary("Comments and events, oldest first. The newest page comes first; pass before=<first id> for older ones. filter=All|Comments|History.");

        issues.MapPost("/comments", async (string key, [FromForm] string? body, IFormFileCollection? files, CreateCommentHandler handler, CancellationToken ct) =>
            {
                var (incoming, tooLarge) = await FormFiles.ReadAsync(files, ct);
                if (tooLarge is not null)
                {
                    return tooLarge;
                }

                return (await handler.HandleAsync(new CreateCommentCommand(key, body, incoming), ct))
                    .ToHttp(comment => TypedResults.Created($"/api/comments/{comment.Id}", comment));
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(FormFiles.MaxRequestBytes))
            .WithSummary("Comment (multipart: \"body\" with @user.name mentions, up to 5 \"files\"). Any role, any sprint state.");

        issues.MapGet("/related", async (string key, RelatedIssuesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(key, ct)).ToHttp(related => TypedResults.Ok(related)))
            .WithSummary("Other open issues of the same module – same screen first.");

        app.MapPut("/api/comments/{id:int}", async (int id, EditCommentRequest request, EditCommentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new EditCommentCommand(id, request.Body), ct)).ToHttp(comment => TypedResults.Ok(comment)))
            .WithTags("Timeline")
            .WithSummary("Edit your own comment within 10 minutes.");

        return app;
    }
}
