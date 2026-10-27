namespace QaTracker.Domain.Enums;

/// <summary>Entry in a sprint's own history. Stored by name (nvarchar) in the database.</summary>
public enum SprintEventKind
{
    Started,
    TestingCompleted,
    ReopenedForTesting,
    Closed,
}
