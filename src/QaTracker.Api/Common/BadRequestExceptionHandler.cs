using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace QaTracker.Api.Common;

/// <summary>
/// Requests the endpoint cannot even read – body that is not JSON / not UTF-8, a value of the wrong type or an unknown
/// enum value, a route or query value that does not parse, a body over the size limit – answer 400 (or 413) as
/// ProblemDetails with code <c>malformed_request</c>, never 500. Minimal APIs throw <see cref="BadHttpRequestException"/>
/// for these because <see cref="RouteHandlerOptions.ThrowOnBadRequest"/> is on in every environment (Program.cs).
/// The message never contains .NET type names; a JSON error names the field (<c>$.severity</c> → <c>severity</c>).
/// </summary>
public sealed class BadRequestExceptionHandler(ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public const string Code = "malformed_request";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        logger.LogInformation("Malformed request {Method} {Path}: {Reason}", httpContext.Request.Method, httpContext.Request.Path, badRequest.Message);

        var status = badRequest.StatusCode is >= 400 and < 500 ? badRequest.StatusCode : StatusCodes.Status400BadRequest;
        var extensions = new Dictionary<string, object?> { ["code"] = Code };
        IResult result = Describe(badRequest) switch
        {
            ({ } field, var message) when status == StatusCodes.Status400BadRequest =>
                TypedResults.ValidationProblem(
                    new Dictionary<string, string[]> { [field] = [message] },
                    detail: message,
                    title: "The request could not be read",
                    extensions: extensions),
            (_, var message) => TypedResults.Problem(
                detail: message,
                statusCode: status,
                title: status == StatusCodes.Status413PayloadTooLarge ? "Request too large" : "The request could not be read",
                extensions: extensions),
        };

        await result.ExecuteAsync(httpContext);
        return true;
    }

    private static (string? Field, string Message) Describe(BadHttpRequestException exception)
    {
        if (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, "The request is larger than the server accepts.");
        }

        if (exception.InnerException is JsonException json)
        {
            var field = FieldName(json.Path);
            return field is null
                ? (null, "The request body is not valid JSON (it must be UTF-8 encoded).")
                : (field, $"The value of '{field}' is not valid.");
        }

        if (exception.InnerException is not null)
        {
            return (null, "The request body could not be read. Send UTF-8 JSON.");
        }

        // Route / query / header binding ("Failed to bind parameter "int page" from "abc".").
        return (null, "A value in the request has the wrong format.");
    }

    /// <summary>"$.items[0].severity" → "items[0].severity"; "$" or none → null.</summary>
    private static string? FieldName(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "$")
        {
            return null;
        }

        return path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
    }
}
