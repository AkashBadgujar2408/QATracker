namespace QaTracker.Domain.Enums;

/// <summary>Why a disputed issue was rejected. Stored by name (nvarchar) in the database.</summary>
public enum RejectType
{
    Invalid,
    Duplicate,
    AsDesigned,
    WontFix,
}
