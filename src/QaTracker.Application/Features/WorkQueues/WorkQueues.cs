using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.WorkQueues;

/// <summary>An issue in a work queue (My work, deploy notice candidates).</summary>
public sealed record WorkItemDto(
    int Id,
    string Key,
    string Title,
    int ProjectId,
    string ProjectKey,
    string ProjectName,
    string ClientName,
    int SprintId,
    string SprintName,
    string ModuleName,
    string? ScreenName,
    Severity Severity,
    Priority Priority,
    IssueStatus Status,
    int? AssigneeId,
    string? AssigneeName,
    string ReporterName,
    DateTimeOffset? DueDate,
    int ReopenCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion)
{
    public static readonly Expression<Func<Issue, WorkItemDto>> Projection = i => new WorkItemDto(
        i.Id,
        i.IssueKey,
        i.Title,
        i.ProjectId,
        i.Project.ProjectKey,
        i.Project.Name,
        i.Project.Client.Name,
        i.SprintId,
        i.Sprint.Name,
        i.Module.Name,
        i.ScreenName,
        i.Severity,
        i.Priority,
        i.Status,
        i.AssigneeId,
        i.Assignee != null ? i.Assignee.FullName : null,
        i.Reporter.FullName,
        i.DueDate,
        i.ReopenCount,
        i.CreatedAt,
        i.UpdatedAt,
        Convert.ToBase64String(i.RowVersion));
}

/// <summary>One tab: the total and the first <see cref="MyWorkHandler.MaxItems"/> issues (most urgent first).</summary>
public sealed record WorkQueueDto(int Count, IReadOnlyList<WorkItemDto> Items);

/// <summary>My work tabs (plan 8.2): Assigned to me · Available to pick · Need info answered · Reopened · Overdue.</summary>
public sealed record MyWorkDto(
    WorkQueueDto Assigned,
    WorkQueueDto Available,
    WorkQueueDto NeedInfoAnswered,
    WorkQueueDto Reopened,
    WorkQueueDto Overdue);

public sealed record MyWorkQuery(int? ProjectId = null);

internal static class WorkQueueQueries
{
    /// <summary>Issues developers can act on: their sprint has completed testing (not in Testing, not Closed).</summary>
    public static IQueryable<Issue> Actionable(IApplicationDbContext db, int? projectId)
    {
        var issues = db.Issues.AsNoTracking().Where(i => i.Sprint.Status == SprintStatus.TestingCompleted);
        return projectId is { } id ? issues.Where(i => i.ProjectId == id) : issues;
    }

    /// <summary>Most urgent first: severity, then due date (none last), then oldest.</summary>
    public static IQueryable<Issue> ByUrgency(IQueryable<Issue> issues) => issues
        .OrderBy(i => i.Severity == Severity.Critical ? 0 : i.Severity == Severity.High ? 1 : i.Severity == Severity.Medium ? 2 : i.Severity == Severity.Low ? 3 : 4)
        .ThenBy(i => i.DueDate == null)
        .ThenBy(i => i.DueDate)
        .ThenBy(i => i.Id);
}

/// <summary>
/// The developer's daily queues. Only issues of sprints that completed testing – while a sprint is in Testing a
/// developer cannot act on its issues yet (pre-assignments show up once testing completes).
/// </summary>
public sealed class MyWorkHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public const int MaxItems = 100;

    public async Task<Result<MyWorkDto>> HandleAsync(MyWorkQuery query, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var now = clock.UtcNow;
        var issues = WorkQueueQueries.Actionable(db, query.ProjectId);
        var awaitingFix = IssueStates.AwaitingFix.ToList();
        var mine = issues.Where(i => i.AssigneeId == me);

        var assigned = await LoadAsync(mine.Where(i => awaitingFix.Contains(i.Status)), cancellationToken);

        // Testers cannot pick; their tab stays empty.
        var pickable = IssueWorkflow.FromStatuses(IssueAction.Pick).ToList();
        var available = currentUser.Role == UserRole.Tester
            ? new WorkQueueDto(0, [])
            : await LoadAsync(issues.Where(i => i.AssigneeId == null && pickable.Contains(i.Status)), cancellationToken);

        // Waiting for information and someone else has commented since the question was asked.
        var needInfoAnswered = await LoadAsync(
            mine.Where(i => i.Status == IssueStatus.NeedInfo
                && i.IssueActivities.Any(c => c.Kind == ActivityKind.Comment && c.UserId != me
                    && c.Id > i.IssueActivities
                        .Where(a => a.Kind == ActivityKind.StatusChanged && a.NewValue == nameof(IssueStatus.NeedInfo))
                        .Max(a => (int?)a.Id))),
            cancellationToken);

        var reopened = await LoadAsync(mine.Where(i => i.Status == IssueStatus.Reopened), cancellationToken);
        var overdue = await LoadAsync(mine.Where(i => i.DueDate != null && i.DueDate < now && awaitingFix.Contains(i.Status)), cancellationToken);

        return new MyWorkDto(assigned, available, needInfoAnswered, reopened, overdue);
    }

    private static async Task<WorkQueueDto> LoadAsync(IQueryable<Issue> issues, CancellationToken cancellationToken)
    {
        var count = await issues.CountAsync(cancellationToken);
        var items = count == 0
            ? []
            : await WorkQueueQueries.ByUrgency(issues).Take(MaxItems).Select(WorkItemDto.Projection).ToListAsync(cancellationToken);
        return new WorkQueueDto(count, items);
    }
}

/// <summary>A pending retest in the queue.</summary>
public sealed record RetestItemDto(
    int DeploymentId,
    int IssueId,
    string IssueKey,
    string Title,
    string ModuleName,
    string? ScreenName,
    string? PageUrl,
    Severity Severity,
    Priority Priority,
    int ReopenCount,
    int ReporterId,
    string ReporterName,
    string? AssigneeName,
    string DeployedByName,
    DateTimeOffset DeployedAt,
    string? Note,
    string? Version,
    bool CanRetest);

/// <summary>Pending retests on one environment URL – open the URL once and clear them all (plan 7.4).</summary>
public sealed record RetestGroupDto(
    int EnvironmentId,
    string EnvironmentName,
    EnvironmentType EnvironmentType,
    string EnvironmentUrl,
    int ProjectId,
    string ProjectKey,
    string ProjectName,
    string ClientName,
    DateTimeOffset OldestDeployedAt,
    IReadOnlyList<RetestItemDto> Items);

public sealed record RetestQueueDto(int Count, IReadOnlyList<RetestGroupDto> Groups);

/// <param name="ReportedByMe">Only issues the current user reported.</param>
public sealed record RetestQueueQuery(int? ProjectId = null, bool ReportedByMe = false);

/// <summary>
/// Tester's retest queue (plan 7.4, 8.2): pending deployments grouped by environment, oldest waiting first. A group's
/// items are in deployment order; <c>canRetest</c> comes from IssueWorkflow (Verify) for the current user.
/// </summary>
public sealed class RetestQueueHandler(IApplicationDbContext db, ICurrentUser currentUser)
{
    public const int MaxItems = 500;

    public async Task<Result<RetestQueueDto>> HandleAsync(RetestQueueQuery query, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var deployments = db.Deployments.AsNoTracking()
            .Where(d => d.RetestResult == RetestResult.Pending
                && d.Issue.Status == IssueStatus.Fixed
                && d.Issue.Sprint.Status == SprintStatus.TestingCompleted);
        if (query.ProjectId is { } projectId)
        {
            deployments = deployments.Where(d => d.Issue.ProjectId == projectId);
        }

        if (query.ReportedByMe)
        {
            deployments = deployments.Where(d => d.Issue.ReporterId == me);
        }

        var rows = await deployments
            .OrderBy(d => d.DeployedAt).ThenBy(d => d.Id)
            .Take(MaxItems)
            .Select(d => new
            {
                d.EnvironmentId,
                EnvironmentName = d.Environment.Name,
                EnvironmentType = d.Environment.Type,
                EnvironmentUrl = d.Environment.Url,
                d.Issue.ProjectId,
                d.Issue.Project.ProjectKey,
                ProjectName = d.Issue.Project.Name,
                ClientName = d.Issue.Project.Client.Name,
                d.Issue.AssigneeId,
                Item = new RetestItemDto(
                    d.Id,
                    d.IssueId,
                    d.Issue.IssueKey,
                    d.Issue.Title,
                    d.Issue.Module.Name,
                    d.Issue.ScreenName,
                    d.Issue.PageUrl,
                    d.Issue.Severity,
                    d.Issue.Priority,
                    d.Issue.ReopenCount,
                    d.Issue.ReporterId,
                    d.Issue.Reporter.FullName,
                    d.Issue.Assignee != null ? d.Issue.Assignee.FullName : null,
                    d.DeployedBy.FullName,
                    d.DeployedAt,
                    d.Note,
                    d.Version,
                    false),
            })
            .ToListAsync(cancellationToken);

        var groups = rows
            .GroupBy(r => r.EnvironmentId)
            .Select(g =>
            {
                var first = g.First();
                var items = g.Select(r => r.Item with
                {
                    CanRetest = IssueWorkflow.Check(
                        new WorkflowContext(IssueStatus.Fixed, SprintStatus.TestingCompleted, r.AssigneeId, currentUser.Role, me),
                        IssueAction.Verify).Allowed,
                }).ToList();
                return new RetestGroupDto(
                    first.EnvironmentId, first.EnvironmentName, first.EnvironmentType, first.EnvironmentUrl,
                    first.ProjectId, first.ProjectKey, first.ProjectName, first.ClientName, first.Item.DeployedAt, items);
            })
            .OrderBy(g => g.OldestDeployedAt)
            .ToList();

        return new RetestQueueDto(rows.Count, groups);
    }
}
