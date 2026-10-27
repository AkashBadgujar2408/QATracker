namespace QaTracker.Domain.Enums;

/// <summary>Tester's verdict on a deployment. Stored by name (nvarchar) in the database.</summary>
public enum RetestResult
{
    Pending,
    Passed,
    Failed,
}
