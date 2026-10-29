using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Reports;
using QaTracker.Application.Features.Sprints;

namespace QaTracker.Api.Endpoints;

public sealed record StartSprintRequest(string Name, int PhaseId, int? DefaultEnvironmentId, string? Note);
public sealed record CompleteTestingRequest(string RowVersion, string? Note);
public sealed record ReopenTestingRequest(string RowVersion, string Note);

/// <summary>
/// Testing sprints (plan 14 SPRINTS, plan 6). Everyone signed in can read; testers and admins start sprints and mark
/// testing completed; only admins reopen testing. Rule violations (second Testing sprint …) are 422.
/// </summary>
public static class SprintsEndpoints
{
    public static IEndpointRouteBuilder MapSprintsEndpoints(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/api/projects/{projectId:int}/sprints").WithTags("Sprints");

        projects.MapGet("/", async (int projectId, ListSprintsHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(projectId, ct)).ToHttp(sprints => TypedResults.Ok(sprints)));

        projects.MapGet("/suggested-name", async (int projectId, int phaseId, SuggestSprintNameHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(projectId, phaseId, ct)).ToHttp(suggestion => TypedResults.Ok(suggestion)))
            .WithSummary("Name for a new sprint of this phase, e.g. \"Sprint 2 – UAT\" or \"Sprint 1 – UAT Round 2\".");

        projects.MapPost("/", async (int projectId, StartSprintRequest request, StartSprintHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new StartSprintCommand(projectId, request.Name, request.PhaseId, request.DefaultEnvironmentId, request.Note), ct))
                    .ToHttp(sprint => TypedResults.Created($"/api/sprints/{sprint.Id}", sprint)))
            .RequireAuthorization(Policies.TesterOrAdmin)
            .WithSummary("Start a testing sprint (422 while another sprint of the project is in Testing).");

        var sprints = app.MapGroup("/api/sprints").WithTags("Sprints");

        sprints.MapGet("/{id:int}", async (int id, GetSprintHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(id, ct)).ToHttp(sprint => TypedResults.Ok(sprint)));

        sprints.MapGet("/{id:int}/completion-summary", async (int id, CompletionSummaryHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttp(summary => TypedResults.Ok(summary)))
            .WithSummary("Counts by severity and module, unassigned and \"Not assessed\" issues – shown before completing testing.");

        sprints.MapPost("/{id:int}/complete-testing", async (int id, CompleteTestingRequest request, CompleteTestingHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CompleteTestingCommand(id, request.RowVersion, request.Note), ct)).ToHttp(sprint => TypedResults.Ok(sprint)))
            .RequireAuthorization(Policies.TesterOrAdmin)
            .WithSummary("Mark Testing Completed: sets due dates from the SLA and notifies members and assignees.");

        sprints.MapPost("/{id:int}/reopen-testing", async (int id, ReopenTestingRequest request, ReopenTestingHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ReopenTestingCommand(id, request.RowVersion, request.Note), ct)).ToHttp(sprint => TypedResults.Ok(sprint)))
            .RequireAuthorization(Policies.AdminOnly)
            .WithSummary("Admin override: put a completed sprint back into Testing (note required, logged).");

        sprints.MapGet("/{id:int}/report", async (int id, string? format, SprintReportHandler handler, CancellationToken ct) =>
                ReportFormats.TryParse(format, out var reportFormat)
                    ? (await handler.HandleAsync(id, reportFormat, ct)).ToHttp(file => TypedResults.File(file.Content, file.ContentType, file.FileName))
                    : ReportFormats.Invalid())
            .WithSummary("Sprint report as Excel (summary, a sheet per module, all issues) or PDF (cover, summary, issues with screenshots): ?format=xlsx|pdf.");

        app.MapGet("/api/lookups/phases", async (PhaseLookupHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithTags("Lookups")
            .WithSummary("Active sprint phases in order.");

        return app;
    }
}
