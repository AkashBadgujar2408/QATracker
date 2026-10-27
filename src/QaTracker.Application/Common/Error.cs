namespace QaTracker.Application.Common;

/// <summary>How an error maps to HTTP (plan 13.1: 400 validation, 401, 403, 404, 409 conflict, 422 rule violation).</summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Rule,
}

/// <summary>Typed, user-safe error returned by handlers instead of throwing.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Field errors for <see cref="ErrorType.Validation"/> (field name → messages).</summary>
    public IReadOnlyDictionary<string, string[]>? Fields { get; init; }

    public static Error Validation(string field, string message) =>
        new("validation", message, ErrorType.Validation) { Fields = new Dictionary<string, string[]> { [field] = [message] } };

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string what) => new("not_found", $"{what} was not found.", ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Rule(string code, string message) => new(code, message, ErrorType.Rule);

    public static Error Concurrency(string what) =>
        new("concurrency", $"{what} was changed by someone else. Reload to see the latest version.", ErrorType.Conflict);
}
