using System.Text;
using Microsoft.Net.Http.Headers;

namespace QaTracker.Api.Common;

/// <summary>
/// A body whose Content-Type names a charset .NET does not know would fail deep inside JSON binding with an
/// InvalidOperationException (500). It is answered here as 400 ProblemDetails (<see cref="BadRequestExceptionHandler.Code"/>)
/// before any endpoint runs. Bodies without a charset, or with UTF-8 / another known one, pass.
/// </summary>
public static class RequestCharsetGuard
{
    public static IApplicationBuilder UseRequestCharsetGuard(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var mediaType)
            && mediaType.Charset is { Length: > 0 } charset
            && !IsKnown(charset.Value!.Trim('"')))
        {
            await TypedResults.Problem(
                    detail: "The request body uses an unknown character set. Send UTF-8 JSON.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The request could not be read",
                    extensions: new Dictionary<string, object?> { ["code"] = BadRequestExceptionHandler.Code })
                .ExecuteAsync(http);
            return;
        }

        await next(http);
    });

    private static bool IsKnown(string charset)
    {
        try
        {
            _ = Encoding.GetEncoding(charset);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
