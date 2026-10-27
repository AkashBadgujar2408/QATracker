using System.Text.Json;
using QaTracker.Application.Common;

namespace QaTracker.Api.Common;

/// <summary>Maps handler results to HTTP: success → callback, <see cref="Error"/> → RFC 9457 ProblemDetails.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    public static IResult ToHttp(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error.Type == ErrorType.Validation)
        {
            var fields = (error.Fields ?? new Dictionary<string, string[]> { [string.Empty] = [error.Message] })
                .ToDictionary(f => CamelCase(f.Key), f => f.Value);
            return TypedResults.ValidationProblem(fields, detail: error.Message, extensions: extensions);
        }

        var (status, title) = error.Type switch
        {
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Rule => (StatusCodes.Status422UnprocessableEntity, "Not allowed"),
            _ => (StatusCodes.Status400BadRequest, "Bad request"),
        };
        return TypedResults.Problem(detail: error.Message, statusCode: status, title: title, extensions: extensions);
    }

    /// <summary>"Contacts[0].Email" → "contacts[0].email" to match the camelCase JSON the client sends.</summary>
    private static string CamelCase(string path) =>
        string.Join('.', path.Split('.').Select(part => JsonNamingPolicy.CamelCase.ConvertName(part)));
}
