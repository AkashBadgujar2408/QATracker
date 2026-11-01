using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QaTracker.Api.IntegrationTests.Infrastructure;

namespace QaTracker.Api.IntegrationTests;

/// <summary>S32: the API serves the Angular build from wwwroot in production (ADR-042).</summary>
public sealed class WebAppHostingTests(ApiFactory api) : IDisposable
{
    private const string InlineScript = "document.title='x'";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _webRoot = CreateWebRoot();

    private static string CreateWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "qa-tracker-tests", "wwwroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"),
            $"<!doctype html><html><head><script>{InlineScript}</script></head><body><app-root></app-root><script src=\"main-KB5LRITQ.js\" type=\"module\"></script></body></html>");
        File.WriteAllText(Path.Combine(root, "main-KB5LRITQ.js"), "console.log('app');");
        File.WriteAllText(Path.Combine(root, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        return root;
    }

    private HttpClient Browser() => api.WithWebHostBuilder(b => b.UseWebRoot(_webRoot))
        .CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Theory]
    [InlineData("/")]
    [InlineData("/issues/PG-12")]
    [InlineData("/dashboards/overview")]
    public async Task DeepLinks_AnswerIndexHtml_WithTheAppPolicy(string path)
    {
        using var browser = Browser();

        using var response = await browser.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("<app-root>");
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();

        var csp = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(InlineScript)));
        csp.ShouldContain($"script-src 'self' 'sha256-{hash}'");
        csp.ShouldContain("connect-src 'self'");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldNotContain("unsafe-eval");
    }

    [Fact]
    public async Task HashedBundles_AreCachedForAYear_AndPublic()
    {
        using var browser = Browser();

        using var bundle = await browser.GetAsync("/main-KB5LRITQ.js", Ct);
        using var icon = await browser.GetAsync("/favicon.svg", Ct);

        bundle.StatusCode.ShouldBe(HttpStatusCode.OK);
        bundle.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromDays(365));
        icon.StatusCode.ShouldBe(HttpStatusCode.OK);
        icon.Headers.CacheControl!.NoCache.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/hubs/unknown")]
    public async Task UnknownApiUrlsAndFiles_StayNotFound(string path)
    {
        using var browser = Browser();

        using var response = await browser.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (response.Content.Headers.ContentType?.MediaType).ShouldNotBe("text/html");
    }

    [Fact]
    public async Task MissingBundle_IsNeverAnsweredWithTheApp()
    {
        using var browser = Browser();

        // Outside the fallback pattern (a file name): no endpoint, so the fallback policy answers – never index.html.
        using var response = await browser.GetAsync("/missing-chunk-ABCDEFGH.js", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiAndHealth_StillAnswerAsBefore()
    {
        using var browser = Browser();

        (await browser.GetAsync("/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.GetAsync("/api/issues", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await browser.PostAsync("/issues/PG-12", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithoutABuild_NoFallbackIsMapped()
    {
        using var browser = api.CreateBrowser();

        // No endpoint at all: the secure-by-default fallback policy answers (401), as before S32.
        (await browser.GetAsync("/issues/PG-12", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
