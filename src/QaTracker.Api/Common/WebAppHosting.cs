using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace QaTracker.Api.Common;

/// <summary>
/// Production hosting of the Angular build from <c>wwwroot</c> (ADR-042): one site, one origin, so the app's relative
/// <c>/api</c> and <c>/hubs</c> URLs, the Secure refresh cookie and SignalR work without CORS. Nothing is mapped when
/// <c>wwwroot/index.html</c> is missing (development, integration tests) – the Angular dev server serves the app there.
/// </summary>
public static partial class WebAppHosting
{
    private const string IndexFile = "index.html";

    /// <summary>Hashed bundle names (<c>main-KB5LRITQ.js</c>, <c>chunk-7FLB4OKi.js</c>) never change content.</summary>
    [GeneratedRegex(@"-[A-Za-z0-9_-]{8}\.(js|css)$")]
    private static partial Regex HashedAsset();

    [GeneratedRegex(@"<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex InlineScript();

    public static bool HasWebApp(this IWebHostEnvironment environment) =>
        environment.WebRootFileProvider.GetFileInfo(IndexFile).Exists;

    /// <summary>Static files of the build – before authentication: the bundles are public, the data behind /api is not.</summary>
    public static IApplicationBuilder UseWebAppFiles(this WebApplication app)
    {
        if (!app.Environment.HasWebApp())
        {
            return app;
        }

        return app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                headers.CacheControl = HashedAsset().IsMatch(context.File.Name)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        });
    }

    /// <summary>
    /// Every other GET outside /api and /hubs answers index.html (deep links such as /issues/PG-12 after a reload).
    /// Unknown /api URLs stay 404 – a missing endpoint must never look like a page.
    /// </summary>
    public static void MapWebApp(this WebApplication app)
    {
        if (!app.Environment.HasWebApp())
        {
            return;
        }

        var index = app.Environment.WebRootFileProvider.GetFileInfo(IndexFile);
        var contentSecurityPolicy = BuildContentSecurityPolicy(index);

        app.MapFallback(async http =>
            {
                var request = http.Request;
                if (request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/hubs") ||
                    !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var headers = http.Response.Headers;
                headers.ContentType = "text/html; charset=utf-8";
                headers.CacheControl = "no-cache";
                headers.ContentSecurityPolicy = contentSecurityPolicy;
                if (HttpMethods.IsGet(request.Method))
                {
                    await http.Response.SendFileAsync(index, http.RequestAborted);
                }
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Scripts only from this origin plus the hashes of the build's inline scripts (Angular's critical-CSS loader).
    /// Inline styles are allowed: Angular Material and the CDK set element styles at run time.
    /// </summary>
    public static string BuildContentSecurityPolicy(IFileInfo index)
    {
        using var stream = index.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = reader.ReadToEnd();

        var hashes = InlineScript().Matches(html)
            .Select(m => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value)))}'")
            .Distinct();

        return string.Join("; ",
            "default-src 'self'",
            $"script-src {string.Join(' ', ["'self'", .. hashes])}",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data: blob:",
            "font-src 'self' data:",
            "connect-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }
}
