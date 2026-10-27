namespace QaTracker.Domain.Enums;

/// <summary>How bad an issue is (plan 7.1). Stored by name (nvarchar) in the database.</summary>
public enum Severity
{
    Critical,
    High,
    Medium,
    Low,
    NotAssessed,
}
