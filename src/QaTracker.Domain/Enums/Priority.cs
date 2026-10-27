namespace QaTracker.Domain.Enums;

/// <summary>How soon an issue should be handled (plan 7.1). Stored by name (nvarchar) in the database.</summary>
public enum Priority
{
    High,
    Medium,
    Low,
}
