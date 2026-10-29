using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Issues;

/// <summary>Kanban board of one project (plan S20).</summary>
/// <param name="SprintId">One sprint; without it every sprint of the project that is not closed.</param>
/// <param name="Mine">Only issues assigned to the current user (ignored when <see cref="AssigneeId"/> is set).</param>
/// <param name="Search">Key, title or screen contains the text.</param>
public sealed record BoardQuery(
    int ProjectId,
    int? SprintId = null,
    int? AssigneeId = null,
    bool Unassigned = false,
    bool Mine = false,
    IReadOnlyList<Severity>? Severity = null,
    string? Search = null);

public sealed class BoardValidator : AbstractValidator<BoardQuery>
{
    public BoardValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0).WithMessage("Choose a project.");
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

/// <summary>A card: what the board shows and where the current user may move it (IssueWorkflow – the UI never repeats the rules).</summary>
/// <param name="Moves">Allowed actions that change the status, i.e. the columns the card may be dropped on.</param>
public sealed record BoardCardDto(
    int Id,
    string Key,
    string Title,
    int SprintId,
    string SprintName,
    SprintStatus SprintStatus,
    string ModuleName,
    string? ScreenName,
    Severity Severity,
    Priority Priority,
    IssueStatus Status,
    int? AssigneeId,
    string? AssigneeName,
    string? AssigneeAvatarColor,
    DateTimeOffset? DueDate,
    bool Overdue,
    int ReopenCount,
    string RowVersion,
    IReadOnlyList<AllowedActionDto> Moves);

/// <summary>A status column: the total and the first <see cref="BoardHandler.MaxCardsPerColumn"/> cards.</summary>
public sealed record BoardColumnDto(IssueStatus Status, int Count, IReadOnlyList<BoardCardDto> Cards);

/// <summary>Columns in workflow order; every status is present, also when empty.</summary>
public sealed record BoardDto(int ProjectId, IReadOnlyList<BoardColumnDto> Columns);

/// <summary>Board columns in the order work flows (plan 7.2). Cards: most severe first, then due date, then oldest.</summary>
public sealed class BoardHandler(IValidator<BoardQuery> validator, IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public const int MaxCardsPerColumn = 200;

    public static readonly IReadOnlyList<IssueStatus> ColumnOrder =
    [
        IssueStatus.New, IssueStatus.Reopened, IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.NeedInfo,
        IssueStatus.OnHold, IssueStatus.Disputed, IssueStatus.Fixed, IssueStatus.Verified, IssueStatus.Closed,
        IssueStatus.Rejected,
    ];

    public async Task<Result<BoardDto>> HandleAsync(BoardQuery query, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == query.ProjectId)
            .Select(p => new { p.RequireLiveVerification })
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return Error.NotFound("Project");
        }

        var issues = Filter(db.Issues.AsNoTracking().Where(i => i.ProjectId == query.ProjectId), query);
        var counts = await issues
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        // Small columns in one query; a column over the limit (usually Closed) gets its own TOP query.
        var small = counts.Where(c => c.Value <= MaxCardsPerColumn).Select(c => c.Key).ToList();
        var rows = small.Count > 0
            ? await CardRows(issues.Where(i => small.Contains(i.Status))).ToListAsync(cancellationToken)
            : [];
        foreach (var big in counts.Where(c => c.Value > MaxCardsPerColumn).Select(c => c.Key))
        {
            rows.AddRange(await CardRows(issues.Where(i => i.Status == big)).Take(MaxCardsPerColumn).ToListAsync(cancellationToken));
        }

        var now = clock.UtcNow;
        var cards = rows
            .Select(x => new BoardCardDto(
                x.Id, x.IssueKey, x.Title, x.SprintId, x.SprintName, x.SprintStatus, x.ModuleName, x.ScreenName, x.Severity, x.Priority,
                x.Status, x.AssigneeId, x.AssigneeName, x.AssigneeAvatarColor, x.DueDate, IssueStates.IsOverdue(x.Status, x.DueDate, now),
                x.ReopenCount, Convert.ToBase64String(x.RowVersion),
                Moves(new WorkflowContext(x.Status, x.SprintStatus, x.AssigneeId, currentUser.Role, currentUser.UserId, project.RequireLiveVerification, x.HasPassedLiveRetest))))
            .ToLookup(c => c.Status);

        var columns = ColumnOrder
            .Select(status => new BoardColumnDto(status, counts.GetValueOrDefault(status), cards[status].ToList()))
            .ToList();
        return new BoardDto(query.ProjectId, columns);
    }

    private IQueryable<Issue> Filter(IQueryable<Issue> issues, BoardQuery query)
    {
        issues = query.SprintId is { } sprintId
            ? issues.Where(i => i.SprintId == sprintId)
            : issues.Where(i => i.Sprint.Status != SprintStatus.Closed);

        if (query.AssigneeId is { } assigneeId)
        {
            issues = issues.Where(i => i.AssigneeId == assigneeId);
        }
        else if (query.Mine)
        {
            var me = currentUser.UserId;
            issues = issues.Where(i => i.AssigneeId == me);
        }
        else if (query.Unassigned)
        {
            issues = issues.Where(i => i.AssigneeId == null);
        }

        if (query.Severity is { Count: > 0 } severities)
        {
            issues = issues.Where(i => severities.Contains(i.Severity));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            issues = issues.Where(i => i.IssueKey.Contains(search) || i.Title.Contains(search) || (i.ScreenName != null && i.ScreenName.Contains(search)));
        }

        return issues;
    }

    private sealed record CardRow(
        int Id,
        string IssueKey,
        string Title,
        int SprintId,
        string SprintName,
        SprintStatus SprintStatus,
        string ModuleName,
        string? ScreenName,
        Severity Severity,
        Priority Priority,
        IssueStatus Status,
        int? AssigneeId,
        string? AssigneeName,
        string? AssigneeAvatarColor,
        DateTimeOffset? DueDate,
        int ReopenCount,
        byte[] RowVersion,
        bool HasPassedLiveRetest);

    private static IQueryable<CardRow> CardRows(IQueryable<Issue> issues) => issues
        .OrderBy(i => i.Severity == Severity.Critical ? 0 : i.Severity == Severity.High ? 1 : i.Severity == Severity.Medium ? 2 : i.Severity == Severity.Low ? 3 : 4)
        .ThenBy(i => i.DueDate == null).ThenBy(i => i.DueDate).ThenBy(i => i.Id)
        .Select(i => new CardRow(
            i.Id,
            i.IssueKey,
            i.Title,
            i.SprintId,
            i.Sprint.Name,
            i.Sprint.Status,
            i.Module.Name,
            i.ScreenName,
            i.Severity,
            i.Priority,
            i.Status,
            i.AssigneeId,
            i.Assignee != null ? i.Assignee.FullName : null,
            i.Assignee != null ? i.Assignee.AvatarColor : null,
            i.DueDate,
            i.ReopenCount,
            i.RowVersion,
            i.Deployments.Any(d => d.Environment.Type == EnvironmentType.LIVE && d.RetestResult == RetestResult.Passed)));

    /// <summary>Actions that move the card to another column (re-assigning in place is not a move).</summary>
    private static IReadOnlyList<AllowedActionDto> Moves(WorkflowContext context) =>
        IssueWorkflow.AllowedActions(context)
            .Where(t => t.Target != context.Status)
            .Select(AllowedActionDto.From)
            .ToList();
}
