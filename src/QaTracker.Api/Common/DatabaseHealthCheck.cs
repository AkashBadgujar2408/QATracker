using Microsoft.Extensions.Diagnostics.HealthChecks;
using QaTracker.Application.Abstractions;

namespace QaTracker.Api.Common;

/// <summary>/health answers 503 when SQL Server cannot be reached – the deploy smoke test and monitoring rely on it.</summary>
public sealed class DatabaseHealthCheck(IApplicationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database is not reachable.");
}
