namespace QaTracker.Domain.Enums;

/// <summary>Kind of issue timeline entry (plan 7.5). Stored by name (nvarchar) in the database.</summary>
public enum ActivityKind
{
    Created,
    Comment,
    StatusChanged,
    Assigned,
    FieldChanged,
    Deployed,
    RetestPassed,
    RetestFailed,
    Attachment,
    CarriedForward,
    SprintEvent,
}
