namespace QaTracker.Application.Abstractions;

/// <summary>Allocates the next issue number of a project (PGERP-142) under a lock, so parallel creates never collide.</summary>
public interface IIssueNumbering
{
    /// <summary>
    /// Takes the project's numbering lock, calls <paramref name="save"/> with the next number and commits.
    /// <paramref name="save"/> adds the issue and saves; the lock is held until it returns.
    /// </summary>
    Task<T> SaveWithNextNumberAsync<T>(int projectId, Func<int, Task<T>> save, CancellationToken cancellationToken);
}
