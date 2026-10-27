namespace QaTracker.Domain.Enums;

/// <summary>Issue lifecycle status (plan 7.2). Stored by name (nvarchar) in the database.</summary>
public enum IssueStatus
{
    New,
    Assigned,
    InProgress,
    NeedInfo,
    OnHold,
    Disputed,
    Fixed,
    Verified,
    Closed,
    Reopened,
    Rejected,
}
