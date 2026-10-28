using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Attachments;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Enums;
using SkiaSharp;

namespace QaTracker.Api.IntegrationTests.Attachments;

public sealed class AttachmentsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, SprintDto Sprint, IssueDto Issue);

    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Files client {Guid.NewGuid():N}" }, Ct)).ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "F" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct)).ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var module = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var phase = (await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>())[0];
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1", phaseId = phase.Id }, Ct)).ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);
        var issue = await (await tester.PostAsJsonAsync("/api/issues", new { sprintId = sprint.Id, moduleId = module.Id, type = "Bug", title = "Total wrong", actualResult = "Shows 0", severity = "High", priority = "High" }, Ct))
            .ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created);
        return new Setup(tester, sprint, issue);
    }

    private static byte[] Png(int width = 1200, int height = 800)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static MultipartFormDataContent Files(params (string Name, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, content) in files)
        {
            form.Add(new ByteArrayContent(content), "files", name);
        }

        return form;
    }

    private static async Task<List<AttachmentDto>> UploadAsync(HttpClient client, string key, params (string Name, byte[] Content)[] files) =>
        await (await client.PostAsync($"/api/issues/{key}/attachments", Files(files), Ct)).ShouldBeOkAsync<List<AttachmentDto>>(HttpStatusCode.Created);

    [Fact]
    public async Task PastedScreenshot_IsStored_WithAThumbnail_ReadableThroughTheSignedUrl()
    {
        var s = await SeedAsync();

        var uploaded = (await UploadAsync(s.Tester, s.Issue.Key, ("screenshot-2026-10-28.png", Png()))).Single();

        uploaded.IsImage.ShouldBeTrue();
        uploaded.ContentType.ShouldBe("image/png");
        uploaded.ThumbnailUrl.ShouldNotBeNull();

        // A plain browser request (no bearer) – as <img src> does.
        using var browser = api.CreateBrowser();
        var thumbnail = await browser.GetAsync(uploaded.ThumbnailUrl, Ct);
        thumbnail.StatusCode.ShouldBe(HttpStatusCode.OK);
        thumbnail.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        using (var decoded = SKBitmap.Decode(await thumbnail.Content.ReadAsByteArrayAsync(Ct)))
        {
            Math.Max(decoded.Width, decoded.Height).ShouldBe(320);
        }

        var original = await browser.GetAsync(uploaded.Url, Ct);
        original.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        original.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        (await original.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBe((int)uploaded.SizeBytes);

        var listed = await (await s.Tester.GetAsync($"/api/issues/{s.Issue.Key}/attachments", Ct)).ShouldBeOkAsync<List<AttachmentDto>>();
        listed.Single().FileName.ShouldBe("screenshot-2026-10-28.png");

        await using var db = api.CreateDbContext();
        (await db.IssueActivities.CountAsync(a => a.IssueId == s.Issue.Id && a.Kind == ActivityKind.Attachment, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Download_WithABadOrMissingSignature_IsRefused()
    {
        var s = await SeedAsync();
        var uploaded = (await UploadAsync(s.Tester, s.Issue.Key, ("notes.txt", "Steps: open, save"u8.ToArray()))).Single();
        using var browser = api.CreateBrowser();

        var tampered = uploaded.Url.Replace("sig=", "sig=x", StringComparison.Ordinal);
        (await (await browser.GetAsync(tampered, Ct)).ShouldBeProblemAsync(HttpStatusCode.Forbidden)).Code().ShouldBe("attachment_forbidden");

        var otherAttachment = uploaded.Url.Replace($"/api/attachments/{uploaded.Id}?", $"/api/attachments/{uploaded.Id + 1000}?", StringComparison.Ordinal);
        (await browser.GetAsync(otherAttachment, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await browser.GetAsync($"/api/attachments/{uploaded.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A signed-in user may use the plain URL.
        var developer = await api.ClientForAsync(UserRole.Developer);
        (await developer.GetAsync($"/api/attachments/{uploaded.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SignedUrl_StopsWorking_WhenTheUserIsDeactivated()
    {
        var s = await SeedAsync();
        var uploaded = (await UploadAsync(s.Tester, s.Issue.Key, ("a.txt", "x"u8.ToArray()))).Single();
        await using (var db = api.CreateDbContext())
        {
            var userId = int.Parse(System.Web.HttpUtility.ParseQueryString(new Uri("https://x" + uploaded.Url).Query)["u"]!, System.Globalization.CultureInfo.InvariantCulture);
            var user = await db.Users.SingleAsync(u => u.Id == userId, Ct);
            user.IsActive = false;
            await db.SaveChangesAsync(Ct);
        }

        using var browser = api.CreateBrowser();
        (await browser.GetAsync(uploaded.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Upload_ChecksContent_RoleAndSprintState()
    {
        var s = await SeedAsync();

        var fake = await s.Tester.PostAsync($"/api/issues/{s.Issue.Key}/attachments", Files(("invoice.png", "MZ\0\0fake"u8.ToArray())), Ct);
        (await fake.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("files")[0].GetString()!.ShouldContain("does not look like a PNG");

        var script = await s.Tester.PostAsync($"/api/issues/{s.Issue.Key}/attachments", Files(("page.html", "<script>alert(1)</script>"u8.ToArray())), Ct);
        (await script.ShouldBeProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("files")[0].GetString()!.ShouldContain("only");

        var developer = await api.ClientForAsync(UserRole.Developer);
        (await developer.PostAsync($"/api/issues/{s.Issue.Key}/attachments", Files(("a.png", Png(10, 10))), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var sprint = await (await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}", Ct)).ShouldBeOkAsync<SprintDetailDto>();
        (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = sprint.Sprint.RowVersion }, Ct)).EnsureSuccessStatusCode();
        var late = await s.Tester.PostAsync($"/api/issues/{s.Issue.Key}/attachments", Files(("a.png", Png(10, 10))), Ct);
        (await late.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("sprint_not_testing");
    }

    [Fact]
    public async Task RemovingAnAttachment_OrTheIssue_DeletesTheFiles()
    {
        var s = await SeedAsync();
        var uploaded = await UploadAsync(s.Tester, s.Issue.Key, ("one.png", Png(50, 50)), ("two.pdf", "%PDF-1.4 test"u8.ToArray()));
        string[] Paths()
        {
            using var db = api.CreateDbContext();
            return db.Attachments.Where(a => a.IssueId == s.Issue.Id).AsEnumerable()
                .SelectMany(a => new[] { a.StoragePath, a.ThumbnailPath }).OfType<string>()
                .Select(p => Path.Combine(api.StorageRoot, p)).ToArray();
        }

        var before = Paths();
        before.Length.ShouldBe(3); // png, its thumbnail, pdf
        before.ShouldAllBe(p => File.Exists(p));

        (await s.Tester.DeleteAsync($"/api/attachments/{uploaded[0].Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        before.Count(File.Exists).ShouldBe(1);

        (await s.Tester.DeleteAsync($"/api/issues/{s.Issue.Key}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        before.ShouldAllBe(p => !File.Exists(p));
    }
}
