namespace QaTracker.Domain.Enums;

/// <summary>Notification event (plan 8.3). Stored by name (nvarchar) in the database.</summary>
public enum NotificationType
{
    TestingCompleted,
    Assigned,
    Mentioned,
    Fixed,
    Reopened,
    NeedInfo,
    Disputed,
    Comment,
    CriticalUnassigned,
    Overdue,
}
