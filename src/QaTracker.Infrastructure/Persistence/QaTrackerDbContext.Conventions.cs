using Microsoft.EntityFrameworkCore;
using QaTracker.Domain.Enums;

namespace QaTracker.Infrastructure.Persistence;

// Hand-written part of the scaffolded context (never edit QaTrackerDbContext.cs – it is regenerated).
public partial class QaTrackerDbContext
{
    /// <summary>All enums in QaTracker.Domain.Enums – stored by name in nvarchar columns guarded by CHECK constraints.</summary>
    internal static readonly IReadOnlyList<Type> DomainEnumTypes = typeof(IssueStatus).Assembly
        .GetTypes()
        .Where(type => type.IsEnum && type.Namespace == typeof(IssueStatus).Namespace)
        .ToArray();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        foreach (var enumType in DomainEnumTypes)
        {
            configurationBuilder.Properties(enumType).HaveConversion<string>();
        }
    }
}
