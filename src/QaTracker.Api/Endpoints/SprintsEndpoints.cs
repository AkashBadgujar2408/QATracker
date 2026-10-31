using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Reports;
using QaTracker.Application.Features.Sprints;

namespace QaTracker.Api.Endpoints;

public sealed record StartSprintRequest(string Name, int PhaseId, int? DefaultEnvironmentId, string? Note);
public sealed record CompleteTestingRequest(string RowVersion, string? Note);
public sealed record ReopenTestingRequest(string RowVersion, string Note);
public sealed record CarryForwardRequest(int TargetSprintId, IReadOnlyList<IssueVersion> Issues, string? Note);
public sealed record CloseSprintRequest(string RowVersion, string? Note, CarryForwardPart? CarryForward);

/// <summary>
/// Testing sprints (plan 14 SPRINTS, plan 6). Everyone signed in can read; testers and admins start sprints and mark
/// testing completed, carry issues forward and close; only admins reopen testing. Rule violations (second Testing sprint …) are 422.
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

        sprints.MapGet("/{id:int}/open-issues", async (int id, OpenIssuesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttp(issues => TypedResults.Ok(issues)))
            .WithSummary("Issues that are neither Closed nor Rejected, with their versions – what closing would carry forward.");

        sprints.MapPost("/{id:int}/carry-forward", async (int id, CarryForwardRequest request, CarryForwardHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CarryForwardCommand(id, request.TargetSprintId, request.Issues ?? [], request.Note), ct)).ToHttp(result => TypedResults.Ok(result)))
            .RequireAuthorization(Policies.TesterOrAdmin)
            .WithSummary("Move open issues of a completed sprint to another open sprint of the project (they keep their key; 409 when an issue changed).");

        sprints.MapPost("/{id:int}/close", async (int id, CloseSprintRequest request, CloseSprintHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CloseSprintCommand(id, request.RowVersion, request.Note, request.CarryForward), ct)).ToHttp(sprint => TypedResults.Ok(sprint)))
            .RequireAuthorization(Policies.TesterOrAdmin)
            .WithSummary("Close a completed sprint (422 while issues are open) – optionally carrying the open ones forward first. Freezes the report numbers.");

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
