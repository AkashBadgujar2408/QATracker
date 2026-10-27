namespace QaTracker.Domain.Enums;

/// <summary>Testing sprint lifecycle state (plan 6). Stored by name (nvarchar) in the database.</summary>
public enum SprintStatus
{
    Testing,
    TestingCompleted,
    Closed,
}
