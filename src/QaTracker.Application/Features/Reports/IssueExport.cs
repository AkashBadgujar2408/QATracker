using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Issues;
using QaTracker.Domain.Enums;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Reports;

public sealed record IssueExportRow(
    string Key,
    string Title,
    string Client,
    string Project,
    string Sprint,
    string Module,
    string? Screen,
    IssueType Type,
    Severity Severity,
    Priority Priority,
    IssueStatus Status,
    string? Assignee,
    string Reporter,
    string? Environment,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int ReopenCount);

/// <param name="Filters">The filters in words ("Status: New, Assigned · Severity: Critical"), printed above the table.</param>
public sealed record IssueExport(IReadOnlyList<IssueExportRow> Rows, string Filters, DateTimeOffset GeneratedAt);

/// <summary>The issue list as an Excel file with the list's current filters and sort (paging ignored).</summary>
public sealed class ExportIssuesHandler(IValidator<ListIssuesQuery> validator, IApplicationDbContext db, IReportRenderer renderer, IClock clock)
{
    public const int MaxRows = 10_000;

    public async Task<Result<ReportFile>> HandleAsync(ListIssuesQuery query, CancellationToken cancellationToken)
    {
        query = query with { Page = 1, PageSize = ListIssuesQuery.DefaultPageSize };
        if (await validator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var issues = ListIssuesHandler.Filter(db.Issues.AsNoTracking(), query, now);
        if (await issues.CountAsync(cancellationToken) > MaxRows)
        {
            return Error.Rule("export_too_large", $"More than {MaxRows:N0} issues match. Narrow the filters and export again.");
        }

        var rows = await ListIssuesHandler.Sort(issues, query.Sort)
            .Select(i => new IssueExportRow(
                i.IssueKey,
                i.Title,
                i.Project.Client.Name,
                i.Project.ProjectKey,
                i.Sprint.Name,
                i.Module.Name,
                i.ScreenName,
                i.Type,
                i.Severity,
                i.Priority,
                i.Status,
                i.Assignee == null ? null : i.Assignee.FullName,
                i.Reporter.FullName,
                i.FoundOnEnvironment == null ? null : i.FoundOnEnvironment.Name,
                i.DueDate,
                i.CreatedAt,
                i.UpdatedAt,
                i.ReopenCount))
            .ToListAsync(cancellationToken);

        var export = new IssueExport(rows, await DescribeAsync(query, cancellationToken), now);
        var name = ReportNames.File($"Issues {ReportText.Date(now)}");
        return new ReportFile(renderer.IssueListExcel(export), ReportNames.XlsxContentType, name + ".xlsx");
    }

    private async Task<string> DescribeAsync(ListIssuesQuery q, CancellationToken cancellationToken)
    {
        var parts = new List<string>();
        if (q.ClientId is { } clientId)
        {
            parts.Add("Client: " + await db.Clients.Where(c => c.Id == clientId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken));
        }

        if (q.ProjectId is { } projectId)
        {
            parts.Add("Project: " + await db.Projects.Where(p => p.Id == projectId).Select(p => p.ProjectKey).FirstOrDefaultAsync(cancellationToken));
        }

        if (q.SprintId is { } sprintId)
        {
            parts.Add("Sprint: " + await db.TestingSprints.Where(s => s.Id == sprintId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken));
        }

        if (q.ModuleId is { } moduleId)
        {
            parts.Add("Module: " + await db.Modules.Where(m => m.Id == moduleId).Select(m => m.Name).FirstOrDefaultAsync(cancellationToken));
        }

        if (q.Status is { Count: > 0 })
        {
            parts.Add("Status: " + string.Join(", ", q.Status.Select(ReportText.Status)));
        }

        if (q.Severity is { Count: > 0 })
        {
            parts.Add("Severity: " + string.Join(", ", q.Severity.Select(ReportText.Severity)));
        }

        if (q.Priority is { Count: > 0 })
        {
            parts.Add("Priority: " + string.Join(", ", q.Priority));
        }

        if (q.AssigneeId is { } assigneeId)
        {
            parts.Add("Assignee: " + await db.Users.Where(u => u.Id == assigneeId).Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken));
        }
        else if (q.Unassigned)
        {
            parts.Add("Unassigned");
        }

        if (q.Overdue)
        {
            parts.Add("Overdue only");
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            parts.Add($"Search: \"{q.Search.Trim()}\"");
        }

        return parts.Count == 0 ? "All issues" : string.Join(" · ", parts);
    }
}
