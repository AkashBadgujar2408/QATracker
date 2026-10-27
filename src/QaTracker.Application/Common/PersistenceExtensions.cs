using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;

namespace QaTracker.Application.Common;

public static class PersistenceExtensions
{
    /// <summary>Optimistic concurrency: the update only succeeds if the row still has the version the client edited.</summary>
    public static void ExpectRowVersion<TEntity>(this IApplicationDbContext db, TEntity entity, string rowVersion)
        where TEntity : class =>
        db.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(rowVersion);

    /// <summary>
    /// Saves and turns the expected database failures into errors: a concurrency conflict (409 "concurrency") or a
    /// unique-index race that slipped past the handler's own check (409 "duplicate").
    /// </summary>
    public static async Task<Error?> TrySaveAsync(this IApplicationDbContext db, string what, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Concurrency(what);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Error.Conflict("duplicate", $"{what} already exists.");
        }
    }

    /// <summary>RowVersion must be the base64 value the API returned.</summary>
    public static IRuleBuilderOptions<T, string> ValidRowVersion<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty()
        .Must(value => Convert.TryFromBase64String(value, new byte[value.Length], out var length) && length == 8)
        .WithMessage("Row version is invalid – reload and try again.");
}
