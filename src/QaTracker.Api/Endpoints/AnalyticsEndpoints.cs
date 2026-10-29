using QaTracker.Api.Common;
using QaTracker.Application.Features.Analytics;

namespace QaTracker.Api.Endpoints;

/// <summary>Query string shared by the dashboards (plan 9): scope plus an IST date range (yyyy-MM-dd, default last 30 days).</summary>
public sealed class AnalyticsFilterRequest
{
    public int? ClientId { get; init; }
    public int? ProjectId { get; init; }
    public int? SprintId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public AnalyticsFilter ToFilter() => new(ClientId, ProjectId, SprintId, From, To);
}

/// <summary>
/// Dashboards (plan 9, 14 ANALYTICS): chart-ready aggregates computed in the database – the UI never aggregates issue
/// lists. Every signed-in user may read them.
/// </summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var analytics = app.MapGroup("/api/analytics").WithTags("Analytics");

        analytics.MapGet("/overview", async ([AsParameters] AnalyticsFilterRequest r, OverviewHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(r.ToFilter(), ct)).ToHttp(dto => TypedResults.Ok(dto)))
            .WithSummary("Home: KPIs, issues by status group, open by severity, created vs closed per day, open by project, recent activity.");

        analytics.MapGet("/sprints/{sprintId:int}", async (int sprintId, SprintDashboardHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(sprintId, ct)).ToHttp(dto => TypedResults.Ok(dto)))
            .WithSummary("Sprint dashboard: header, progress, burn-down, found per testing day, module × severity, types, reopened, sprint comparison.");

        analytics.MapGet("/developers", async ([AsParameters] AnalyticsFilterRequest r, DevelopersHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(r.ToFilter(), ct)).ToHttp(dto => TypedResults.Ok(dto)))
            .WithSummary("Developer dashboard: workload, fixes per week, average fix time, first-time-right, SLA compliance and your own stats.");

        analytics.MapGet("/quality", async ([AsParameters] AnalyticsFilterRequest r, QualityHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(r.ToFilter(), ct)).ToHttp(dto => TypedResults.Ok(dto)))
            .WithSummary("Quality dashboard: defect leakage by phase, retest turnaround, retest queue ageing, module hotspots, rejected ratio, severity trend.");

        analytics.MapGet("/clients", async ([AsParameters] AnalyticsFilterRequest r, ClientsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(r.ToFilter(), ct)).ToHttp(dto => TypedResults.Ok(dto)))
            .WithSummary("Client health: client cards with health, open by client and severity, UAT vs LIVE per project, average closure time.");

        return app;
    }
}
