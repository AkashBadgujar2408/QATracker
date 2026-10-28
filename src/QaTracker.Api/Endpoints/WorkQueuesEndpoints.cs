using QaTracker.Api.Common;
using QaTracker.Application.Features.WorkQueues;

namespace QaTracker.Api.Endpoints;

public sealed record CreateDeployNoticeRequest(int EnvironmentId, IReadOnlyList<string>? IssueKeys, string? Note, string? Version);

/// <summary>
/// Daily work screens (plan 7.4, 8.2): the developer's My work tabs, the tester's retest queue grouped by environment,
/// and batch deploy notices of the developer's own fixed issues.
/// </summary>
public static class WorkQueuesEndpoints
{
    public static IEndpointRouteBuilder MapWorkQueuesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/work", async (int? projectId, MyWorkHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new MyWorkQuery(projectId), ct)).ToHttp(work => TypedResults.Ok(work)))
            .WithTags("Work queues")
            .WithSummary("My work tabs with counts: assigned, available to pick, need info answered, reopened, overdue (sprints that completed testing).");

        app.MapGet("/api/retest-queue", async (int? projectId, bool? mine, RetestQueueHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RetestQueueQuery(projectId, mine ?? false), ct)).ToHttp(queue => TypedResults.Ok(queue)))
            .WithTags("Work queues")
            .WithSummary("Pending retests grouped by environment, oldest first. mine=true: only issues you reported.");

        var notices = app.MapGroup("/api/deploy-notices").WithTags("Work queues");

        notices.MapGet("/candidates", async (int? projectId, DeployCandidatesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new DeployCandidatesQuery(projectId), ct)).ToHttp(candidates => TypedResults.Ok(candidates)))
            .WithSummary("Your fixed or verified issues with their latest deployment per environment.");

        notices.MapGet("/", async (int? projectId, int? size, ListDeployNoticesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListDeployNoticesQuery(projectId, size ?? ListDeployNoticesQuery.DefaultSize), ct)).ToHttp(list => TypedResults.Ok(list)))
            .WithSummary("Recent deploy notices, newest first.");

        notices.MapPost("/", async (CreateDeployNoticeRequest r, CreateDeployNoticeHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateDeployNoticeCommand(r.EnvironmentId, r.IssueKeys, r.Note, r.Version), ct))
                    .ToHttp(notice => TypedResults.Created($"/api/deploy-notices/{notice.Id}", notice)))
            .WithSummary("Announce your fixed issues on one environment with one note: a pending retest and a timeline entry per issue (all or nothing).");

        return app;
    }
}
