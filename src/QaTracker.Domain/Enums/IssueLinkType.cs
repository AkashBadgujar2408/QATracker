namespace QaTracker.Domain.Enums;

/// <summary>Relation between two issues. Stored by name (nvarchar) in the database.</summary>
public enum IssueLinkType
{
    Duplicate,
    Related,
}
