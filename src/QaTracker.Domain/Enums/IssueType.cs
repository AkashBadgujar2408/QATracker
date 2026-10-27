namespace QaTracker.Domain.Enums;

/// <summary>Kind of issue (plan 7.1). Stored by name (nvarchar) in the database.</summary>
public enum IssueType
{
    Bug,
    Validation,
    UI,
    Security,
    Performance,
    Enhancement,
    Clarification,
}
