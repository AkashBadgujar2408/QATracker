using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Issues;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.Endpoints;

public sealed record CreateIssueRequest(
    int SprintId,
    int ModuleId,
    string? ScreenName,
    int? FoundOnEnvironmentId,
    string? PageUrl,
    IssueType? Type,
    string Title,
    string? Steps,
    string? ExpectedResult,
    string ActualResult,
    string? Impact,
    Severity? Severity,
    Priority? Priority,
    int? AssigneeId);

/// <summary>PUT body; the version goes in the If-Match header.</summary>
public sealed record UpdateIssueRequest(
    int ModuleId,
    string? ScreenName,
    int? FoundOnEnvironmentId,
    string? PageUrl,
    IssueType? Type,
    string Title,
    string? Steps,
    string? ExpectedResult,
    string ActualResult,
    string? Impact,
    Severity? Severity,
    Priority? Priority,
    int? AssigneeId);

/// <summary>Query string of GET /api/issues (plan 14 names: envId, q, size).</summary>
public sealed class IssueListRequest
{
    public int? ClientId { get; init; }
    public int? ProjectId { get; init; }
    public int? SprintId { get; init; }
    public int? ModuleId { get; init; }
    public int? EnvId { get; init; }
    public IssueStatus[]? Status { get; init; }
    public Severity[]? Severity { get; init; }
    public Priority[]? Priority { get; init; }
    public int? AssigneeId { get; init; }
    public bool? Unassigned { get; init; }
    public bool? Overdue { get; init; }
    public string? Q { get; init; }
    public int? Page { get; init; }
    public int? Size { get; init; }
    public string? Sort { get; init; }

    public ListIssuesQuery ToQuery() => new(
        ClientId,
        ProjectId,
        SprintId,
        ModuleId,
        EnvId,
        Status,
        Severity,
        Priority,
        AssigneeId,
        Unassigned ?? false,
        Overdue ?? false,
        Q,
        Page ?? 1,
        Size ?? ListIssuesQuery.DefaultPageSize,
        Sort);
}

/// <summary>
/// Issues (plan 14 ISSUES). Everyone signed in reads; testers and admins create, edit and delete while the sprint is in
/// Testing (otherwise 422). Updates need the version from GET (ETag) in If-Match; a stale version is 409.
/// </summary>
public static class IssuesEndpoints
{
    public static IEndpointRouteBuilder MapIssuesEndpoints(this IEndpointRouteBuilder app)
    {
        var issues = app.MapGroup("/api/issues").WithTags("Issues");

        issues.MapGet("/", async ([AsParameters] IssueListRequest request, ListIssuesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request.ToQuery(), ct)).ToHttp(page => TypedResults.Ok(page)))
            .WithSummary("Filter (status/severity/priority repeat for several values), sort (\"-dueDate\") and page issues.");

        issues.MapPost("/", async (CreateIssueRequest r, CreateIssueHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(
                    new CreateIssueCommand(r.SprintId, r.ModuleId, r.ScreenName, r.FoundOnEnvironmentId, r.PageUrl, r.Type, r.Title, r.Steps,
                        r.ExpectedResult, r.ActualResult, r.Impact, r.Severity, r.Priority, r.AssigneeId), ct))
                    .ToHttp(issue => WithETag(http, issue, TypedResults.Created($"/api/issues/{issue.Key}", issue))))
            .RequireAuthorization(Policies.TesterOrAdmin);

        issues.MapGet("/{key}", async (string key, GetIssueHandler handler, HttpContext http, CancellationToken ct) =>
            (await handler.HandleAsync(key, ct)).ToHttp(issue => WithETag(http, issue, TypedResults.Ok(issue))));

        issues.MapPut("/{key}", async (string key, UpdateIssueRequest r, [FromHeader(Name = "If-Match")] string? ifMatch, UpdateIssueHandler handler, HttpContext http, CancellationToken ct) =>
            {
                if (ParseIfMatch(ifMatch) is not { } rowVersion)
                {
                    return TypedResults.Problem(
                        detail: "Send the issue's version (ETag from GET) in the If-Match header.",
                        statusCode: StatusCodes.Status428PreconditionRequired,
                        title: "Version required",
                        extensions: new Dictionary<string, object?> { ["code"] = "if_match_required" });
                }

                return (await handler.HandleAsync(
                        new UpdateIssueCommand(key, rowVersion, r.ModuleId, r.ScreenName, r.FoundOnEnvironmentId, r.PageUrl, r.Type, r.Title, r.Steps,
                            r.ExpectedResult, r.ActualResult, r.Impact, r.Severity, r.Priority, r.AssigneeId), ct))
                    .ToHttp(issue => WithETag(http, issue, TypedResults.Ok(issue)));
            })
            .RequireAuthorization(Policies.TesterOrAdmin);

        issues.MapDelete("/{key}", async (string key, DeleteIssueHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new DeleteIssueCommand(key), ct)).ToHttp(_ => TypedResults.NoContent()))
            .RequireAuthorization(Policies.TesterOrAdmin)
            .WithSummary("Delete an issue of a sprint that is still in Testing.");

        app.MapGet("/api/projects/{projectId:int}/screens", async (int projectId, int? moduleId, string? q, ScreenSuggestionsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ScreenSuggestionQuery(projectId, moduleId, q), ct)).ToHttp(screens => TypedResults.Ok(screens)))
            .WithTags("Issues")
            .WithSummary("Screen names used in the project – the module's first, most used first.");

        return app;
    }

    private static IResult WithETag(HttpContext http, IssueDto issue, IResult result)
    {
        http.Response.Headers.ETag = $"\"{issue.RowVersion}\"";
        return result;
    }

    /// <summary>"\"AAAAAAAAB9E=\"" or W/"…" → the base64 row version.</summary>
    private static string? ParseIfMatch(string? header) =>
        EntityTagHeaderValue.TryParse(header, out var tag) && !tag.Tag.Equals("*", StringComparison.Ordinal)
            ? tag.Tag.ToString().Trim('"')
            : null;
}
