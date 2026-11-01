using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Attachments;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Application.Features.Timeline;
using QaTracker.Domain.Enums;
using SkiaSharp;

namespace QaTracker.Api.IntegrationTests.Security;

/// <summary>
/// S30 security pass (plan 15.2): who may call what (every endpoint), unreadable requests, response headers, rate
/// limits and upload naming. Ownership checks of single features live next to them (notifications, attachments).
/// </summary>
public sealed class SecurityTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Endpoints a browser may call without an access token.</summary>
    private static readonly HashSet<string> PublicEndpoints =
    [
        "GET /health",
        "POST /api/auth/login",
        "POST /api/auth/refresh",
        "POST /api/auth/logout",
        // Signed URL for <img src>; without credentials it answers 401 itself (AttachmentsTests).
        "GET /api/attachments/{id:int}",
        "GET /api/attachments/{id:int}/thumbnail",
    ];

    private static string ReviewedPoliciesPath => Path.Combine(AppContext.BaseDirectory, "Security", "endpoint-policies.txt");

    private IEnumerable<(string Method, RouteEndpoint Endpoint)> Endpoints() =>
        api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m, e)))
            .Where(x => x.m != HttpMethods.Options)
            // OpenAPI document and Scalar UI exist in Development only.
            .Where(x => !Route(x.e).StartsWith("/scalar", StringComparison.Ordinal) && !Route(x.e).StartsWith("/openapi", StringComparison.Ordinal));

    private static string Route(RouteEndpoint endpoint)
    {
        var route = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }

    /// <summary>A concrete URL for the pattern (any value – the call must be refused before it is looked at).</summary>
    private static string Url(RouteEndpoint endpoint)
    {
        var url = Route(endpoint);
        foreach (var parameter in endpoint.RoutePattern.Parameters)
        {
            var value = parameter.Name switch
            {
                "key" => "SEC-1",
                "date" => "2026-11-02",
                "documentName" => "v1",
                _ => "1",
            };
            url = System.Text.RegularExpressions.Regex.Replace(url, "\\{" + parameter.Name + "[^}]*\\}", value);
        }

        return url;
    }

    private static string Policy(RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return "anonymous";
        }

        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy ?? (a.Roles is null ? "default" : $"roles:{a.Roles}"))
            .Distinct()
            .ToList();

        // The group's SignedIn/default policy plus a stricter one: the stricter one is what matters.
        var strict = policies.Where(p => p is not ("default" or "Authenticated")).ToList();
        return strict.Count > 0 ? string.Join("+", strict) : "Authenticated";
    }

    [Fact]
    public async Task EveryEndpoint_RefusesAnonymousCallers_ExceptTheReviewedPublicOnes()
    {
        using var anonymous = api.CreateBrowser();
        var leaks = new List<string>();
        var checkedCount = 0;

        foreach (var (method, endpoint) in Endpoints())
        {
            var name = $"{method} {Route(endpoint)}";
            if (PublicEndpoints.Contains(name) || Route(endpoint).StartsWith("/hubs", StringComparison.Ordinal))
            {
                continue;
            }

            using var request = new HttpRequestMessage(new HttpMethod(method), Url(endpoint));
            if (method != HttpMethods.Get && method != HttpMethods.Delete)
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var response = await anonymous.SendAsync(request, Ct);
            checkedCount++;
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                leaks.Add($"{name} → {(int)response.StatusCode}");
            }
        }

        checkedCount.ShouldBeGreaterThan(90);
        leaks.ShouldBeEmpty();
    }

    [Fact]
    public async Task HubNegotiation_RefusesAnonymousCallers()
    {
        using var anonymous = api.CreateBrowser();
        (await anonymous.PostAsync("/hubs/tracker/negotiate?negotiateVersion=1", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Every endpoint's authorization policy against the reviewed list (Security/endpoint-policies.txt). A new endpoint
    /// or a changed policy fails here until the list is reviewed and updated – write access is then a conscious decision.
    /// Endpoints marked "authenticated" are open to every role; their handlers enforce ownership or the workflow rules
    /// (comments: author only; workflow: IssueWorkflow by role and sprint state; notifications: own only).
    /// </summary>
    [Fact]
    public void EndpointPolicies_MatchTheReviewedList()
    {
        var actual = Endpoints()
            .Select(x => $"{x.Method,-6} {Route(x.Endpoint)} | {Policy(x.Endpoint)}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        var reviewed = File.ReadAllLines(ReviewedPoliciesPath)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        var added = actual.Except(reviewed).ToList();
        var removed = reviewed.Except(actual).ToList();
        (added.Count + removed.Count).ShouldBe(
            0,
            $"Not reviewed:\n{string.Join('\n', added)}\nNo longer present:\n{string.Join('\n', removed)}\n\nFull list:\n{string.Join('\n', actual)}");
    }

    private sealed record Setup(HttpClient Tester, HttpClient Developer, HttpClient Admin, ProjectDto Project, ModuleDto Module, SprintDto Sprint, IssueDto Issue);

    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var developer = await api.ClientForAsync(UserRole.Developer);
        var admin = await api.ClientForAsync(UserRole.Admin);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Security client {Guid.NewGuid():N}" }, Ct)).ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "S" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct)).ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var phase = (await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>())[0];
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1", phaseId = phase.Id }, Ct)).ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        var issue = await (await tester.PostAsJsonAsync("/api/issues", new { sprintId = sprint.Id, moduleId = module.Id, type = "Bug", title = "Total wrong", actualResult = "Shows 0", severity = "High", priority = "High" }, Ct))
            .ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);
        return new Setup(tester, developer, admin, project, module, sprint, issue);
    }

    [Fact]
    public async Task Developers_CannotWriteMastersSprintsIssuesAttachmentsOrImport_AndTesters_CannotUseAdminEndpoints()
    {
        var s = await SeedAsync();
        var json = (object body) => JsonContent.Create(body);

        var developerCalls = new (string Name, Func<Task<HttpResponseMessage>> Call)[]
        {
            ("create client", () => s.Developer.PostAsync("/api/clients", json(new { name = "Dev client" }), Ct)),
            ("add module", () => s.Developer.PostAsync($"/api/projects/{s.Project.Id}/modules", json(new { name = "Dev module" }), Ct)),
            ("replace members", () => s.Developer.PutAsync($"/api/projects/{s.Project.Id}/members", json(new { userIds = Array.Empty<int>() }), Ct)),
            ("start sprint", () => s.Developer.PostAsync($"/api/projects/{s.Project.Id}/sprints", json(new { name = "Dev sprint", phaseId = s.Sprint.PhaseId }), Ct)),
            ("complete testing", () => s.Developer.PostAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", json(new { }), Ct)),
            ("close sprint", () => s.Developer.PostAsync($"/api/sprints/{s.Sprint.Id}/close", json(new { }), Ct)),
            ("create issue", () => s.Developer.PostAsync("/api/issues", json(new { sprintId = s.Sprint.Id, moduleId = s.Module.Id, type = "Bug", title = "x", actualResult = "y", severity = "Low", priority = "Low" }), Ct)),
            ("delete issue", () => s.Developer.DeleteAsync($"/api/issues/{s.Issue.Key}", Ct)),
            ("upload", () => s.Developer.PostAsync($"/api/issues/{s.Issue.Key}/attachments", new MultipartFormDataContent(), Ct)),
            ("import preview", () => s.Developer.PostAsync("/api/import/excel/preview", new MultipartFormDataContent(), Ct)),
            ("users", () => s.Developer.GetAsync("/api/users", Ct)),
        };
        var testerCalls = new (string Name, Func<Task<HttpResponseMessage>> Call)[]
        {
            ("users", () => s.Tester.GetAsync("/api/users", Ct)),
            ("create user", () => s.Tester.PostAsync("/api/users", json(new { userName = "x" }), Ct)),
            ("SLA", () => s.Tester.GetAsync("/api/settings/sla", Ct)),
            ("phases", () => s.Tester.PostAsync("/api/settings/phases", json(new { name = "x" }), Ct)),
            ("mail outbox", () => s.Tester.GetAsync("/api/admin/email", Ct)),
            ("mail server", () => s.Tester.GetAsync("/api/admin/email/settings", Ct)),
            ("reopen testing", () => s.Tester.PostAsync($"/api/sprints/{s.Sprint.Id}/reopen-testing", json(new { reason = "x" }), Ct)),
        };

        var allowed = new List<string>();
        foreach (var (who, calls) in new[] { ("developer", developerCalls), ("tester", testerCalls) })
        {
            foreach (var (name, call) in calls)
            {
                using var response = await call();
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    allowed.Add($"{who}: {name} → {(int)response.StatusCode}");
                }
            }
        }

        allowed.ShouldBeEmpty();

        // Nothing changed.
        (await (await s.Admin.GetAsync($"/api/issues/{s.Issue.Key}", Ct)).ShouldBeOkAsync<IssueDto>()).Key.ShouldBe(s.Issue.Key);
    }

    [Fact]
    public async Task Comments_CanOnlyBeEditedByTheirAuthor()
    {
        var s = await SeedAsync();
        var form = new MultipartFormDataContent { { new StringContent("Seen on UAT too."), "body" } };
        var comment = await (await s.Tester.PostAsync($"/api/issues/{s.Issue.Key}/comments", form, Ct)).ShouldBeOkAsync<ActivityDto>(HttpStatusCode.Created);

        await (await s.Developer.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "Not my words" }, Ct)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "not_author");
        await (await s.Admin.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "Not my words either" }, Ct)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "not_author");

        await using var db = api.CreateDbContext();
        (await db.IssueActivities.SingleAsync(a => a.Id == comment.Id, Ct)).Body!.ShouldNotContain("Not my words");
    }

    public static TheoryData<string, string, string, string?> MalformedBodies => new()
    {
        // name, content type, body, field named in the answer
        { "unknown enum value", "application/json", """{"sprintId":1,"moduleId":1,"type":"Bug","title":"x","actualResult":"y","severity":"Catastrophic","priority":"High"}""", "severity" },
        { "number as text", "application/json", """{"sprintId":"one","moduleId":1}""", "sprintId" },
        { "truncated JSON", "application/json", """{"sprintId":1,"title":"x""", null },
        { "not JSON at all", "application/json", "title=x&severity=High", null },
        { "Windows-1252 bytes sent as UTF-8", "application/json; charset=utf-8", "{\"title\":\"Totals \u0096 wrong\"}", null },
        { "unknown charset", "application/json; charset=klingon", """{"title":"x"}""", null },
    };

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task MalformedBodies_Answer400ProblemDetails_Not500(string name, string contentType, string body, string? field)
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var bytes = name.StartsWith("Windows-1252", StringComparison.Ordinal)
            ? Encoding.Latin1.GetBytes(body)
            : Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        var response = await tester.PostAsync("/api/issues", content, Ct);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        problem.Code().ShouldBe("malformed_request");
        var text = problem.ToString();
        text.ShouldNotContain("QaTracker.");
        text.ShouldNotContain("System.");
        if (field is not null)
        {
            problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(text);
        }
    }

    [Fact]
    public async Task UnparsableQueryValues_And_UnknownEnumInQuery_Answer400ProblemDetails()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);

        foreach (var url in new[] { "/api/issues?page=abc", "/api/issues?status=Exploded", "/api/analytics/overview?from=yesterday" })
        {
            var response = await tester.GetAsync(url, Ct);
            (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).Code().ShouldBe("malformed_request", url);
        }
    }

    [Fact]
    public async Task Responses_CarryTheSecurityHeaders_AndNoServerBanner()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        using var anonymous = api.CreateBrowser();

        foreach (var response in new[] { await tester.GetAsync("/api/auth/me", Ct), await anonymous.GetAsync("/api/issues", Ct), await tester.GetAsync("/api/issues/NOPE-1", Ct) })
        {
            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
            response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
            response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
            response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
            response.Headers.Contains("Server").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Login_And_PasswordChange_AreRateLimitedPerAddress_AndTheApiPerUser()
    {
        await using var limited = api.WithWebHostBuilder(builder => builder
            .UseSetting("RateLimiting:LoginPermitLimit", "3")
            .UseSetting("RateLimiting:ApiPermitLimit", "8"));
        var user = await api.CreateUserAsync(UserRole.Tester);
        var browser = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await browser.PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "wrong" }, Ct)).StatusCode);
        }

        statuses[..3].ShouldAllBe(s => s != HttpStatusCode.TooManyRequests);
        var rejected = await browser.PostAsJsonAsync("/api/auth/login", new { user.UserName, password = "Passw0rd!" }, Ct);
        (await rejected.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests)).Code().ShouldBe("rate_limited");
        rejected.Headers.Contains("Retry-After").ShouldBeTrue();

        // Password change shares the sign-in budget of the address.
        var other = await api.CreateUserAsync(UserRole.Tester);
        var (signedIn, _) = await api.LoginAsync(other);
        var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "guess", newPassword = "N3w-Passw0rd!" }),
        };
        change.Headers.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        (await browser.SendAsync(change, Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // General API budget: per user.
        var api429 = 0;
        for (var i = 0; i < 10; i++)
        {
            var me = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/unread-count");
            me.Headers.Authorization = signedIn.DefaultRequestHeaders.Authorization;
            if ((await browser.SendAsync(me, Ct)).StatusCode == HttpStatusCode.TooManyRequests)
            {
                api429++;
            }
        }

        api429.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Uploads_AreStoredUnderRandomNames_WhateverTheClientCallsThem()
    {
        var s = await SeedAsync();
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.Coral);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var form = new MultipartFormDataContent { { new ByteArrayContent(png.ToArray()), "files", "..\\..\\..\\web.config.png" } };

        var uploaded = (await (await s.Tester.PostAsync($"/api/issues/{s.Issue.Key}/attachments", form, Ct)).ShouldBeOkAsync<List<AttachmentDto>>(HttpStatusCode.Created)).Single();

        uploaded.FileName.ShouldNotContain("..");
        uploaded.FileName.ShouldNotContain("\\");
        uploaded.FileName.ShouldNotContain("/");
        await using var db = api.CreateDbContext();
        var stored = await db.Attachments.SingleAsync(a => a.Id == uploaded.Id, Ct);
        stored.StoragePath.ShouldNotContain("..");
        stored.StoragePath.ShouldNotContain("web.config");
        Path.GetFullPath(Path.Combine(api.StorageRoot, stored.StoragePath)).ShouldStartWith(Path.GetFullPath(api.StorageRoot));

        // A script disguised as an image is refused.
        var disguised = new MultipartFormDataContent { { new ByteArrayContent("<script>alert(1)</script>"u8.ToArray()), "files", "photo.png" } };
        (await s.Tester.PostAsync($"/api/issues/{s.Issue.Key}/attachments", disguised, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
