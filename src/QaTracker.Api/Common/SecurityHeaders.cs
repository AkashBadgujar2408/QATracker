namespace QaTracker.Api.Common;

/// <summary>
/// Response headers for every API answer (plan 15.2). The API only serves JSON and files, so the policy is "nothing
/// may run or be framed": <c>default-src 'none'; frame-ancestors 'none'</c>. Attachment downloads set their own,
/// slightly wider CSP (images inline); the Scalar UI (Development only) keeps the browser defaults.
/// HSTS comes from <c>UseHsts</c> outside Development; Kestrel's Server header is switched off in Program.cs.
/// </summary>
public static class SecurityHeaders
{
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((http, next) =>
    {
        http.Response.OnStarting(() =>
        {
            var headers = http.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            if (!IsDocsPath(http.Request.Path) && string.IsNullOrEmpty(headers.ContentSecurityPolicy))
            {
                headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
            }

            return Task.CompletedTask;
        });
        return next(http);
    });

    private static bool IsDocsPath(PathString path) =>
        path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi");
}
