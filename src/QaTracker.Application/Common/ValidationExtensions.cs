using FluentValidation;

namespace QaTracker.Application.Common;

public static class ValidationExtensions
{
    /// <summary>
    /// Runs the validator; returns a validation <see cref="Error"/> with every field message, or null when valid.
    /// Handlers call this first: <c>if (await validator.CheckAsync(command, ct) is { } error) return error;</c>
    /// </summary>
    public static async Task<Error?> CheckAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return null;
        }

        var fields = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return new Error("validation", result.Errors[0].ErrorMessage, ErrorType.Validation) { Fields = fields };
    }
}
