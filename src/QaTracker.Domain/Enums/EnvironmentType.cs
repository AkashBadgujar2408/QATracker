namespace QaTracker.Domain.Enums;

/// <summary>Kind of project environment URL. Stored by name (nvarchar) in the database.</summary>
public enum EnvironmentType
{
    UAT,
    LIVE,
    Other,
}
