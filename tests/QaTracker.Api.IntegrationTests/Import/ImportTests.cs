using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QaTracker.Api.IntegrationTests.Infrastructure;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Import;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Application.Features.Sprints;
using QaTracker.Domain.Enums;
using SkiaSharp;

namespace QaTracker.Api.IntegrationTests.Import;

public sealed class ImportTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(HttpClient Tester, ProjectDto Project, SprintDto Sprint, int DeveloperId, string DeveloperFirstName, int SalesModuleId);

    private async Task<Setup> SeedAsync()
    {
        var tester = await api.ClientForAsync(UserRole.Tester);
        var client = await (await tester.PostAsJsonAsync("/api/clients", new { name = $"Import client {Guid.NewGuid():N}" }, Ct)).ShouldBeOkAsync<ClientDto>(HttpStatusCode.Created);
        var key = "I" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var project = await (await tester.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new { name = "ERP", key }, Ct)).ShouldBeOkAsync<ProjectDto>(HttpStatusCode.Created);
        // An existing module with another spelling: the sheet "Sales & Distribution" goes into it.
        var sales = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/modules", new { name = "sales & distribution" }, Ct)).ShouldBeOkAsync<ModuleDto>(HttpStatusCode.Created);
        var phase = (await (await tester.GetAsync("/api/lookups/phases", Ct)).ShouldBeOkAsync<List<PhaseLookup>>())[0];
        var sprint = await (await tester.PostAsJsonAsync($"/api/projects/{project.Id}/sprints", new { name = "Sprint 1 – UAT", phaseId = phase.Id }, Ct)).ShouldBeOkAsync<SprintDto>(HttpStatusCode.Created);

        // A developer with a first name nobody else has, as "Resolve By" names people.
        var developer = await api.CreateUserAsync(UserRole.Developer);
        var firstName = "Zed" + Guid.NewGuid().ToString("N")[..8];
        await using (var db = api.CreateDbContext())
        {
            await db.Users.Where(u => u.Id == developer.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.FullName, $"{firstName} Developer"), Ct);
        }

        return new Setup(tester, project, sprint, developer.Id, firstName, sales.Id);
    }

    /// <summary>A copy of the legacy layout (Appendix C): header row, "N. Title" blocks in column B, a screenshot.</summary>
    private static byte[] Workbook(string developerFirstName)
    {
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("Sales & Distribution");
        ws.Cell(1, 2).Value = "Pardi Gears – Testing Sheet";
        string[] headers = ["Sr. No", "Issue", "Severity | Priority", "Screenshot", "Resolve By", "Is Complete", "Pending Reason", "Valid/Invalid", "Remarks"];
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(2, c + 1).Value = headers[c];
        }

        ws.Cell(3, 1).Value = 1;
        ws.Cell(3, 2).Value = "1. Invoice total is wrong\nIssue: Open an invoice with two lines\nActual Result: Shows 0\nExpected Result: Shows the total\nImpact: Billing stops";
        ws.Cell(3, 3).Value = "Severity: High<br>Priority: Medium";
        ws.Cell(3, 4).Value = "img B";
        ws.Cell(3, 5).Value = developerFirstName.ToUpperInvariant();
        ws.Cell(3, 6).Value = "No";
        using (var png = new MemoryStream(Png()))
        {
            ws.AddPicture(png, ClosedXML.Excel.Drawings.XLPictureFormat.Png).MoveTo(ws.Cell(3, 4));
        }

        ws.Cell(4, 2).Value = "2. Discount line ignored";
        ws.Cell(5, 2).Value = "Actual Result:";
        ws.Cell(6, 2).Value = "The discount is not subtracted";
        ws.Cell(4, 3).Value = "Severity: Not yet assessed\nPriority: Pending…";
        ws.Cell(4, 6).Value = "Yes";
        ws.Cell(4, 9).Value = "For your refer.";

        ws.Cell(7, 2).Value = "3. Duplicate customer allowed\nActual Result: Two customers with one GST number";
        ws.Cell(7, 3).Value = "Severity: Medium\nPriority: Low";
        ws.Cell(7, 8).Value = "Invalid";

        ws.Cell(8, 2).Value = "4. Report is slow\nActual Result: Takes 2 minutes";
        ws.Cell(8, 3).Value = "Severity: Low\nPriority: Low";
        ws.Cell(8, 7).Value = "Waiting for production data";

        var items = book.AddWorksheet("Item Mast, BOM, Tax Str.Mast");
        for (var c = 0; c < headers.Length; c++)
        {
            items.Cell(1, c + 1).Value = headers[c];
        }

        items.Cell(2, 2).Value = "1. BOM saves twice\nActual Result: Two rows after one save";
        items.Cell(2, 3).Value = "Severity: Critical | Priority: High";
        items.Cell(2, 5).Value = "Nobody";

        book.AddWorksheet("Notes").Cell(1, 1).Value = "No issues here";

        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(640, 400);
        bitmap.Erase(SKColors.IndianRed);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static MultipartFormDataContent Form(int sprintId, byte[] workbook, string? corrections = null, string fileName = "Testing Sheet.xlsx")
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(workbook), "file", fileName },
            { new StringContent(sprintId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "sprintId" },
        };
        if (corrections is not null)
        {
            form.Add(new StringContent(corrections), "corrections");
        }

        return form;
    }

    [Fact]
    public async Task Preview_ReadsSheetsAsModules_BlocksAsIssues_AndAppliesTheAppendixRules()
    {
        var s = await SeedAsync();

        var preview = await (await s.Tester.PostAsync("/api/import/excel/preview", Form(s.Sprint.Id, Workbook(s.DeveloperFirstName)), Ct)).ShouldBeOkAsync<ImportPreview>();

        preview.Modules.Select(m => (m.Sheet, m.Name, m.ExistingModuleId, m.Issues)).ShouldBe([
            ("Sales & Distribution", "sales & distribution", s.SalesModuleId, 4),
            ("Item Mast, BOM, Tax Str.Mast", "Item Mast, BOM, Tax Str.Mast", null, 1),
        ]);
        preview.Rows.Count.ShouldBe(5);

        var invoice = preview.Rows[0];
        invoice.Id.ShouldBe("1-3");
        invoice.Title.ShouldBe("Invoice total is wrong");
        invoice.Steps.ShouldBe("Open an invoice with two lines");
        invoice.ActualResult.ShouldBe("Shows 0");
        invoice.ExpectedResult.ShouldBe("Shows the total");
        invoice.Impact.ShouldBe("Billing stops");
        (invoice.Severity, invoice.Priority).ShouldBe((Severity.High, Priority.Medium));
        invoice.AssigneeId.ShouldBe(s.DeveloperId);
        invoice.Status.ShouldBe(IssueStatus.Assigned);
        invoice.Images.ShouldBe(1);
        invoice.Comments.ShouldBeEmpty();
        invoice.Warnings.ShouldBeEmpty();

        var discount = preview.Rows[1];
        discount.ActualResult.ShouldBe("The discount is not subtracted");
        (discount.Severity, discount.Priority).ShouldBe((Severity.NotAssessed, Priority.Medium));
        discount.Warnings.Count.ShouldBe(2);
        discount.Status.ShouldBe(IssueStatus.Verified);
        discount.Comments.ShouldBe(["For your refer."]);

        preview.Rows[2].Status.ShouldBe(IssueStatus.Rejected);
        preview.Rows[2].RejectType.ShouldBe(RejectType.Invalid);
        preview.Rows[3].Status.ShouldBe(IssueStatus.OnHold);
        preview.Rows[3].HoldReason.ShouldBe("Waiting for production data");

        var bom = preview.Rows[4];
        (bom.Severity, bom.Priority).ShouldBe((Severity.Critical, Priority.High));
        bom.Status.ShouldBe(IssueStatus.New);
        bom.Warnings.ShouldHaveSingleItem().ShouldContain("\"Nobody\" matches no active developer");

        preview.ReadyRows.ShouldBe(3);
    }

    [Fact]
    public async Task Commit_CreatesTheIssuesInOrder_WithModules_Statuses_Comments_ScreenshotsAndActivities()
    {
        var s = await SeedAsync();
        var corrections = $$"""
            {
              "modules": [{ "sheetIndex": 1, "name": "Item Master" }],
              "rows": [
                { "id": "1-4", "severity": "High", "priority": "High" },
                { "id": "1-8", "skip": true },
                { "id": "2-2", "assigneeId": {{s.DeveloperId}} }
              ]
            }
            """;

        var result = await (await s.Tester.PostAsync("/api/import/excel/commit", Form(s.Sprint.Id, Workbook(s.DeveloperFirstName), corrections), Ct)).ShouldBeOkAsync<ImportResult>();

        result.Created.ShouldBe(4);
        result.Skipped.ShouldBe(1);
        result.ModulesCreated.ShouldBe(1);
        result.Attachments.ShouldBe(1);
        result.FirstKey.ShouldBe($"{s.Project.Key}-1");
        result.LastKey.ShouldBe($"{s.Project.Key}-4");

        await using var db = api.CreateDbContext();
        var issues = await db.Issues.Where(i => i.SprintId == s.Sprint.Id).OrderBy(i => i.Number)
            .Select(i => new { i.Title, Module = i.Module.Name, i.Status, i.Severity, i.Priority, i.AssigneeId, i.RejectType, i.VerifiedAt, i.ActualResult, Attachments = i.Attachments.Count })
            .ToListAsync(Ct);
        issues.Select(i => i.Title).ShouldBe(["Invoice total is wrong", "Discount line ignored", "Duplicate customer allowed", "BOM saves twice"]);
        issues[0].Module.ShouldBe("sales & distribution");
        issues[0].Attachments.ShouldBe(1);
        issues[0].ActualResult.ShouldBe("<p>Shows 0</p>");
        (issues[1].Status, issues[1].Severity, issues[1].Priority).ShouldBe((IssueStatus.Verified, Severity.High, Priority.High));
        issues[1].VerifiedAt.ShouldNotBeNull();
        (issues[2].Status, issues[2].RejectType).ShouldBe((IssueStatus.Rejected, RejectType.Invalid));
        (issues[3].Module, issues[3].Status, issues[3].AssigneeId).ShouldBe(("Item Master", IssueStatus.Assigned, s.DeveloperId));

        var discountKey = $"{s.Project.Key}-2";
        var activities = await db.IssueActivities.Where(a => a.Issue.IssueKey == discountKey).OrderBy(a => a.Id).Select(a => new { a.Kind, a.NewValue, a.Body }).ToListAsync(Ct);
        activities.Select(a => a.Kind).ShouldBe([ActivityKind.Created, ActivityKind.StatusChanged, ActivityKind.Comment]);
        activities[1].NewValue.ShouldBe("Verified");
        activities[2].Body.ShouldBe("For your refer.");
    }

    [Fact]
    public async Task Import_IsRefused_ForDevelopers_OutsideTesting_AndForFilesThatAreNotWorkbooks()
    {
        var s = await SeedAsync();
        var developer = await api.ClientForAsync(UserRole.Developer);

        (await developer.PostAsync("/api/import/excel/preview", Form(s.Sprint.Id, Workbook(s.DeveloperFirstName)), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await (await s.Tester.PostAsync("/api/import/excel/preview", Form(s.Sprint.Id, "not a workbook"u8.ToArray()), Ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Tester.PostAsync("/api/import/excel/preview", Form(s.Sprint.Id, Workbook(s.DeveloperFirstName), fileName: "sheet.csv"), Ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Tester.PostAsync("/api/import/excel/commit", Form(s.Sprint.Id, Workbook(s.DeveloperFirstName), "{ not json"), Ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Tester.PostAsync("/api/import/excel/commit", Form(s.Sprint.Id, Workbook(s.DeveloperFirstName), """{ "rows": [{ "id": "9-9", "skip": true }] }"""), Ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Tester.PostAsync("/api/import/excel/preview", Form(999_999, Workbook(s.DeveloperFirstName)), Ct)).ShouldBeProblemAsync(HttpStatusCode.NotFound);

        var completed = await (await s.Tester.PostAsJsonAsync($"/api/sprints/{s.Sprint.Id}/complete-testing", new { rowVersion = s.Sprint.RowVersion }, Ct)).ShouldBeOkAsync<SprintDto>();
        completed.Status.ShouldBe(SprintStatus.TestingCompleted);
        (await s.Tester.PostAsync("/api/import/excel/commit", Form(s.Sprint.Id, Workbook(s.DeveloperFirstName)), Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
