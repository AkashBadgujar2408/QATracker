namespace QaTracker.Domain.Enums;

/// <summary>Entry in a sprint's own history. Stored by name (nvarchar) in the database.</summary>
public enum SprintEventKind
{
    Started,
    TestingCompleted,
    ReopenedForTesting,
    Closed,

    /// <summary>Open issues left this sprint for another sprint of the project (carry forward, plan 6.1).</summary>
    IssuesCarriedForward,

    /// <summary>Issues joined this sprint, carried forward from another one.</summary>
    IssuesCarriedIn,
}
