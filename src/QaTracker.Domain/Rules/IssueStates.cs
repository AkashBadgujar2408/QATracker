using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Rules;

/// <summary>Groups of issue statuses used by lists, due dates and dashboards.</summary>
public static class IssueStates
{
    /// <summary>Waiting for a developer's fix – these count as overdue once the due date has passed.</summary>
    public static readonly IReadOnlyList<IssueStatus> AwaitingFix =
    [
        IssueStatus.New,
        IssueStatus.Assigned,
        IssueStatus.InProgress,
        IssueStatus.NeedInfo,
        IssueStatus.OnHold,
        IssueStatus.Disputed,
        IssueStatus.Reopened,
    ];

    public static bool IsOverdue(IssueStatus status, DateTimeOffset? dueDate, DateTimeOffset now) =>
        dueDate is { } due && due < now && AwaitingFix.Contains(status);
}
