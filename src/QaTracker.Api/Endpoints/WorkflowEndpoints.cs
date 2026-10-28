using Microsoft.AspNetCore.Mvc;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Workflow;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Api.Endpoints;

public sealed record AssignRequest(int UserId, string? Note);

public sealed record TransitionRequest(IssueAction? Action, string? Reason, RejectType? RejectType, string? DuplicateOfKey);

public sealed record FixedRequest(string? Note, IReadOnlyList<int>? EnvironmentIds, string? Version);

public sealed record RetestRequest(bool Passed, string? Comment);

public sealed record BulkAssignRequest(IReadOnlyList<string>? Keys, int UserId, string? Note);

/// <summary>
/// Issue workflow (plan 14 ISSUES, 7.2–7.4). Every signed-in user may call these; IssueWorkflow decides (403 wrong role,
/// 422 not allowed now). Single-issue changes need If-Match (428 without, 409 when stale); pick is atomic instead.
/// </summary>
public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var issues = app.MapGroup("/api/issues").WithTags("Workflow");

        issues.MapPost("/{key}/assign", async (string key, AssignRequest r, [FromHeader(Name = "If-Match")] string? ifMatch, AssignIssueHandler handler, HttpContext http, CancellationToken ct) =>
                IfMatch.Parse(ifMatch) is not { } version
                    ? IfMatch.Required()
                    : (await handler.HandleAsync(new AssignIssueCommand(key, version, r.UserId, r.Note), ct)).ToHttp(issue => Ok(http, issue)))
            .WithSummary("Assign or reassign (New → Assigned). Testers may pre-assign while the sprint is in Testing.");

        issues.MapPost("/{key}/pick", async (string key, PickIssueHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(key, ct)).ToHttp(issue => Ok(http, issue)))
            .WithSummary("Atomic self-assign of an unassigned issue and start it (409 \"Already picked by …\").");

        issues.MapPost("/{key}/transition", async (string key, TransitionRequest r, [FromHeader(Name = "If-Match")] string? ifMatch, TransitionIssueHandler handler, HttpContext http, CancellationToken ct) =>
                IfMatch.Parse(ifMatch) is not { } version
                    ? IfMatch.Required()
                    : (await handler.HandleAsync(new TransitionIssueCommand(key, version, r.Action, r.Reason, r.RejectType, r.DuplicateOfKey), ct)).ToHttp(issue => Ok(http, issue)))
            .WithSummary("Start, NeedInfo, Resume, Hold, Unhold, Dispute, Reject, Contest, Verify, Reopen or Close – see allowedActions.");

        issues.MapPost("/{key}/fixed", async (string key, FixedRequest r, [FromHeader(Name = "If-Match")] string? ifMatch, MarkFixedHandler handler, HttpContext http, CancellationToken ct) =>
                IfMatch.Parse(ifMatch) is not { } version
                    ? IfMatch.Required()
                    : (await handler.HandleAsync(new MarkFixedCommand(key, version, r.Note, r.EnvironmentIds, r.Version), ct)).ToHttp(issue => Ok(http, issue)))
            .WithSummary("Mark fixed with one deployment (retest pending) per environment; also adds a LIVE deployment later.");

        issues.MapPost("/bulk-assign", async (BulkAssignRequest r, BulkAssignHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new BulkAssignCommand(r.Keys, r.UserId, r.Note), ct)).ToHttp(result => TypedResults.Ok(result)))
            .WithSummary("Assign many issues to one person; issues that cannot be assigned are listed with the reason.");

        app.MapPost("/api/deployments/{id:int}/retest", async (int id, RetestRequest r, RetestHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(new RetestCommand(id, r.Passed, r.Comment), ct)).ToHttp(issue => Ok(http, issue)))
            .WithTags("Workflow")
            .WithSummary("Retest passed → Verified (when no other retest is pending); failed → Reopened (comment required).");

        return app;
    }

    private static IResult Ok(HttpContext http, IssueDto issue) => IfMatch.WithETag(http, issue.RowVersion, TypedResults.Ok(issue));
}
