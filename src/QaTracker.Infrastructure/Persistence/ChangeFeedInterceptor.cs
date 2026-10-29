using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Features.Notifications;

namespace QaTracker.Infrastructure.Persistence;

/// <summary>
/// Runs the <see cref="ChangeFeed"/> on every async SaveChanges: notifications are written in the same save, live
/// updates go out after the commit (straight after the save, or when the handler's own transaction commits).
/// A failed save or rolled-back transaction publishes nothing. Publishing errors are logged, never thrown.
/// </summary>
internal sealed class ChangeFeedInterceptor(ChangeFeed feed, IRealtimePublisher publisher, ILogger<ChangeFeedInterceptor> logger)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    /// <summary>True during SaveChanges: the commit of EF's own save transaction is not the handler's commit.</summary>
    private bool _saving;

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is IApplicationDbContext db)
        {
            var tracker = eventData.Context.ChangeTracker;
            tracker.DetectChanges();
            await feed.PrepareAsync(db, tracker.Entries().ToList(), cancellationToken);
        }

        _saving = true;
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        _saving = false;
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            await PublishAsync();
        }

        return result;
    }

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving = false;
        feed.Discard();
        return Task.CompletedTask;
    }

    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (!_saving)
        {
            await PublishAsync();
        }
    }

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        feed.Discard();
        return Task.CompletedTask;
    }

    private async Task PublishAsync()
    {
        if (!feed.HasPending)
        {
            return;
        }

        var events = feed.TakeEvents();
        try
        {
            // The change is committed: a cancelled request must not swallow the live update.
            await publisher.PublishAsync(events, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live update of {Count} events failed", events.Count);
        }
    }
}
