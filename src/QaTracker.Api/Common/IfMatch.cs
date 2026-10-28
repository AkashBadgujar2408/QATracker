using Microsoft.Net.Http.Headers;

namespace QaTracker.Api.Common;

/// <summary>Optimistic concurrency over HTTP: the issue's version (ETag) comes back in If-Match.</summary>
public static class IfMatch
{
    /// <summary>"\"AAAAAAAAB9E=\"" or W/"…" → the base64 row version.</summary>
    public static string? Parse(string? header) =>
        EntityTagHeaderValue.TryParse(header, out var tag) && !tag.Tag.Equals("*", StringComparison.Ordinal)
            ? tag.Tag.ToString().Trim('"')
            : null;

    /// <summary>428 when the header is missing.</summary>
    public static IResult Required() => TypedResults.Problem(
        detail: "Send the issue's version (ETag from GET) in the If-Match header.",
        statusCode: StatusCodes.Status428PreconditionRequired,
        title: "Version required",
        extensions: new Dictionary<string, object?> { ["code"] = "if_match_required" });

    public static IResult WithETag(HttpContext http, string rowVersion, IResult result)
    {
        http.Response.Headers.ETag = $"\"{rowVersion}\"";
        return result;
    }
}
