using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using Severity = QaTracker.Domain.Enums.Severity;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Issues;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Issue list filters (plan 14). Multi-value filters match any of the values; all filters combine with AND.</summary>
/// <param name="Unassigned">Only issues without an assignee (ignored when <see cref="AssigneeId"/> is set).</param>
/// <param name="Overdue">Due date passed and still waiting for a fix.</param>
/// <param name="Search">Key, title or screen contains the text.</param>
/// <param name="Sort">Field name, "-" prefix for descending: key, title, severity, priority, status, dueDate, createdAt, updatedAt.</param>
public sealed record ListIssuesQuery(
    int? ClientId = null,
    int? ProjectId = null,
    int? SprintId = null,
    int? ModuleId = null,
    int? EnvironmentId = null,
    IReadOnlyList<IssueStatus>? Status = null,
    IReadOnlyList<Severity>? Severity = null,
    IReadOnlyList<Priority>? Priority = null,
    int? AssigneeId = null,
    bool Unassigned = false,
    bool Overdue = false,
    string? Search = null,
    int Page = 1,
    int PageSize = ListIssuesQuery.DefaultPageSize,
    string? Sort = null)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;
}

public sealed class ListIssuesValidator : AbstractValidator<ListIssuesQuery>
{
    public static readonly IReadOnlySet<string> SortFields =
        new HashSet<string>(["key", "title", "severity", "priority", "status", "dueDate", "createdAt", "updatedAt"], StringComparer.OrdinalIgnoreCase);

    public ListIssuesValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListIssuesQuery.MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Sort)
            .Must(sort => SortFields.Contains(sort!.TrimStart('-')))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort))
            .WithMessage($"Sort by one of: {string.Join(", ", SortFields)} (prefix \"-\" for descending).");
    }
}

/// <summary>Filtered, sorted, paged issue list (default: newest first).</summary>
public sealed class ListIssuesHandler(IValidator<ListIssuesQuery> validator, IApplicationDbContext db, IClock clock)
{
    public async Task<Result<PagedResult<IssueListItemDto>>> HandleAsync(ListIssuesQuery query, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var issues = Filter(db.Issues.AsNoTracking(), query, clock.UtcNow);
        var total = await issues.CountAsync(cancellationToken);
        var items = await Sort(issues, query.Sort)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(IssueListItemDto.Projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<IssueListItemDto>(items, query.Page, query.PageSize, total);
    }

    internal static IQueryable<Issue> Filter(IQueryable<Issue> issues, ListIssuesQuery query, DateTimeOffset now)
    {
        if (query.ClientId is { } clientId)
        {
            issues = issues.Where(i => i.Project.ClientId == clientId);
        }

        if (query.ProjectId is { } projectId)
        {
            issues = issues.Where(i => i.ProjectId == projectId);
        }

        if (query.SprintId is { } sprintId)
        {
            issues = issues.Where(i => i.SprintId == sprintId);
        }

        if (query.ModuleId is { } moduleId)
        {
            issues = issues.Where(i => i.ModuleId == moduleId);
        }

        if (query.EnvironmentId is { } environmentId)
        {
            issues = issues.Where(i => i.FoundOnEnvironmentId == environmentId);
        }

        if (query.Status is { Count: > 0 } statuses)
        {
            issues = issues.Where(i => statuses.Contains(i.Status));
        }

        if (query.Severity is { Count: > 0 } severities)
        {
            issues = issues.Where(i => severities.Contains(i.Severity));
        }

        if (query.Priority is { Count: > 0 } priorities)
        {
            issues = issues.Where(i => priorities.Contains(i.Priority));
        }

        if (query.AssigneeId is { } assigneeId)
        {
            issues = issues.Where(i => i.AssigneeId == assigneeId);
        }
        else if (query.Unassigned)
        {
            issues = issues.Where(i => i.AssigneeId == null);
        }

        if (query.Overdue)
        {
            var awaitingFix = IssueStates.AwaitingFix.ToList();
            issues = issues.Where(i => i.DueDate != null && i.DueDate < now && awaitingFix.Contains(i.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            issues = issues.Where(i => i.IssueKey.Contains(search) || i.Title.Contains(search) || (i.ScreenName != null && i.ScreenName.Contains(search)));
        }

        return issues;
    }

    /// <summary>Severity and priority sort by importance (Critical first), not alphabetically; ties newest first.</summary>
    internal static IQueryable<Issue> Sort(IQueryable<Issue> issues, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = sort?.TrimStart('-').ToLowerInvariant() ?? "createdat";
        if (sort is null)
        {
            descending = true;
        }

        IOrderedQueryable<Issue> ordered = field switch
        {
            "key" => Order(issues, i => i.Number, descending),
            "title" => Order(issues, i => i.Title, descending),
            "severity" => Order(issues, i => i.Severity == Severity.Critical ? 0 : i.Severity == Severity.High ? 1 : i.Severity == Severity.Medium ? 2 : i.Severity == Severity.Low ? 3 : 4, descending),
            "priority" => Order(issues, i => i.Priority == Priority.High ? 0 : i.Priority == Priority.Medium ? 1 : 2, descending),
            "status" => Order(issues, i => i.Status, descending),
            "duedate" => Order(issues, i => i.DueDate, descending),
            "updatedat" => Order(issues, i => i.UpdatedAt ?? i.CreatedAt, descending),
            _ => Order(issues, i => i.CreatedAt, descending),
        };

        return ordered.ThenByDescending(i => i.Id);
    }

    private static IOrderedQueryable<Issue> Order<TKey>(IQueryable<Issue> issues, System.Linq.Expressions.Expression<Func<Issue, TKey>> key, bool descending) =>
        descending ? issues.OrderByDescending(key) : issues.OrderBy(key);
}
