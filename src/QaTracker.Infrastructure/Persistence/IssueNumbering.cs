using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;

namespace QaTracker.Infrastructure.Persistence;

/// <summary>
/// Issue numbers per project: an exclusive application lock (sp_getapplock) per project for the length of a transaction,
/// then MAX(Number) + 1. The unique index UX_Issues_Project_Number stays the final guard.
/// </summary>
public sealed class IssueNumbering(QaTrackerDbContext db) : IIssueNumbering
{
    private const int LockTimeoutMilliseconds = 10_000;

    public async Task<T> SaveWithNextNumberAsync<T>(int projectId, Func<int, Task<T>> save, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var resource = $"QaTracker.IssueNumber.{projectId}";
        var result = await db.Database
            .SqlQuery<int>($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {LockTimeoutMilliseconds}; SELECT @r AS [Value]")
            .ToListAsync(cancellationToken);
        if (result.Single() < 0)
        {
            throw new TimeoutException($"Could not lock issue numbering of project {projectId}.");
        }

        var next = (await db.Issues.Where(i => i.ProjectId == projectId).MaxAsync(i => (int?)i.Number, cancellationToken) ?? 0) + 1;
        var saved = await save(next);
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }
}
