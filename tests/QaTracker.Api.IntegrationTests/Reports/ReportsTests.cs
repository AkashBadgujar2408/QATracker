using System.Net;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Enums;
using SkiaSharp;

namespace QaTracker.Api.IntegrationTests.Reports;

public sealed class ReportsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, ProjectDto Project, SprintDto Sprint, List<IssueDto> Issues);

    /// <summary>A sprint with two modules ("Sales" and one with characters Excel refuses in sheet names) and three issues, one with a screenshot.</summary>
    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Report client {Guid.NewGuid():N}" }, Ct)).ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "R" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct)).ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        var sales = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Sales" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var items = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "Item Mast/BOM, Tax [Str]" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var phase = (await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>())[0];
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1 – UAT", phaseId = phase.Id }, Ct)).ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);

        var issues = new List<IssueDto>();
        foreach (var (module, title, severity) in new[] { (sales.Id, "Total wrong", "High"), (sales.Id, "Discount ignored", "Critical"), (items.Id, "BOM saves twice", "Low") })
        {
            issues.Add(await (await tester.PostAsJsonAsync("/api/issues", new
            {
                sprintId = sprint.Id,
                moduleId = module,
                type = "Bug",
                title,
                actualResult = "<p>Shows 0</p><p>after saving</p>",
                expectedResult = "<p>Shows the total</p>",
                severity,
                priority = "High",
            }, Ct)).ShouldBeOkAsync<IssueDto>(HttpStatusCode.Created));
        }

        var form = new MultipartFormDataContent { { new ByteArrayContent(Png()), "files", "screenshot.png" } };
        (await tester.PostAsync($"/api/issues/{issues[0].Key}/attachments", form, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        return new Setup(tester, project, sprint, issues);
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(800, 500);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task SprintExcel_HasSummary_ASheetPerModuleLikeTheLegacyWorkbook_AndAllIssues()
    {
        var s = await SeedAsync();

        var response = await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}/report?format=xlsx", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var fileName = response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName!;
        fileName.ShouldStartWith($"{s.Project.Key} Sprint 1 – UAT report ");
        fileName.ShouldEndWith(".xlsx");

        using var book = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        book.Worksheets.Select(w => w.Name).ShouldBe(["Summary", "Item Mast BOM, Tax  Str", "Sales", "All issues"]);

        var summary = book.Worksheet("Summary");
        summary.Cell(5, 2).GetString().ShouldBe("Sprint 1 – UAT");
        summary.CellsUsed().Any(c => c.GetString() == "By module").ShouldBeTrue();

        var sheet = book.Worksheet("Sales");
        sheet.Cell(3, 2).GetString().ShouldBe("Issue");
        var block = sheet.Cell(4, 2).GetString();
        block.ShouldStartWith("1. Total wrong");
        block.ShouldContain("Actual Result: Shows 0\nafter saving");
        block.ShouldContain("Expected Result: Shows the total");
        sheet.Cell(4, 3).GetString().ShouldBe("Severity: High\nPriority: High");
        sheet.Cell(4, 6).GetString().ShouldBe("New");
        sheet.Cell(4, 7).GetString().ShouldBe("No");
        sheet.Cell(4, 9).GetString().ShouldBe("Valid");
        sheet.Cell(4, 10).GetString().ShouldBe(s.Issues[0].Key);
        sheet.Cell(5, 2).GetString().ShouldStartWith("2. Discount ignored");
        sheet.Pictures.Count.ShouldBe(1);

        var all = book.Worksheet("All issues");
        all.RowsUsed().Count().ShouldBe(4);
        all.Cell(2, 12).DataType.ShouldBe(XLDataType.DateTime);
    }

    [Fact]
    public async Task SprintPdf_IsAValidMultiPageDocument()
    {
        var s = await SeedAsync();

        var response = await s.Tester.GetAsync($"/api/sprints/{s.Sprint.Id}/report?format=pdf", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
        var text = Encoding.Latin1.GetString(bytes);
        text.TrimEnd().ShouldEndWith("%%EOF");
        // Cover, summary and the issue list.
        System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b").Count.ShouldBeGreaterThanOrEqualTo(3);
        // The screenshot is embedded as an image.
        text.ShouldContain("/Subtype /Image");
    }

    [Fact]
    public async Task SprintReport_RejectsAnUnknownFormat_AndAnUnknownSprint()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);

        await (await tester.GetAsync("/api/sprints/1/report?format=docx", Ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await tester.GetAsync("/api/sprints/999999/report?format=pdf", Ct)).ShouldBeProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IssueExport_UsesTheListFiltersAndSort_AndDescribesThem()
    {
        var s = await SeedAsync();
        var developer = await api.ClientForAsync(UserRole.Developer);

        var response = await developer.GetAsync($"/api/issues/export?projectId={s.Project.Id}&severity=High&severity=Critical&sort=key&page=3&size=1", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var book = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        var ws = book.Worksheet("Issues");
        ws.Cell(2, 1).GetString().ShouldStartWith($"Project: {s.Project.Key} · Severity: High, Critical · 2 issues");
        ws.Cell(4, 1).GetString().ShouldBe("Key");
        ws.Cell(5, 1).GetString().ShouldBe(s.Issues[0].Key);
        ws.Cell(6, 1).GetString().ShouldBe(s.Issues[1].Key);
        ws.Cell(7, 1).IsEmpty().ShouldBeTrue();
        ws.Cell(5, 9).GetString().ShouldBe("High");
    }
}
