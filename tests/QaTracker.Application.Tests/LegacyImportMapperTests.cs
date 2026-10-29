using QaTracker.Application.Features.Import;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Tests;

public sealed class LegacyImportMapperTests
{
    private static readonly IReadOnlyList<ImportUser> Developers =
    [
        new(1, "Emily Developer"),
        new(2, "Rahul Sharma"),
        new(3, "Rahul Verma"),
        new(4, "Amit Patil"),
    ];

    [Theory]
    [InlineData("High", Severity.High, false)]
    [InlineData("critical.", Severity.Critical, false)]
    [InlineData("Blocker", Severity.Critical, false)]
    [InlineData("Major", Severity.High, false)]
    [InlineData("  medium ", Severity.Medium, false)]
    [InlineData("Minor", Severity.Low, false)]
    [InlineData("Not yet assessed", Severity.NotAssessed, true)]
    [InlineData("Pending…", Severity.NotAssessed, true)]
    [InlineData("", Severity.NotAssessed, true)]
    [InlineData("Severe-ish", Severity.NotAssessed, true)]
    public void Severity_FollowsTheWorkbookWords(string text, Severity expected, bool warns)
    {
        var warnings = new List<string>();

        LegacyImportMapper.ParseSeverity(text, warnings).ShouldBe(expected);
        (warnings.Count > 0).ShouldBe(warns);
    }

    [Theory]
    [InlineData("High", Priority.High, false)]
    [InlineData("Urgent", Priority.High, false)]
    [InlineData("low", Priority.Low, false)]
    [InlineData("Pending...", Priority.Medium, true)]
    [InlineData(null, Priority.Medium, true)]
    [InlineData("Soon", Priority.Medium, true)]
    public void Priority_DefaultsToMediumWithAWarning(string? text, Priority expected, bool warns)
    {
        var warnings = new List<string>();

        LegacyImportMapper.ParsePriority(text, warnings).ShouldBe(expected);
        (warnings.Count > 0).ShouldBe(warns);
    }

    [Fact]
    public void ResolveBy_MatchesOneDeveloperByFirstName()
    {
        var warnings = new List<string>();

        LegacyImportMapper.MatchDeveloper("emily", Developers, warnings)!.Id.ShouldBe(1);
        LegacyImportMapper.MatchDeveloper(" Amit P ", Developers, warnings)!.Id.ShouldBe(4);
        LegacyImportMapper.MatchDeveloper(null, Developers, warnings).ShouldBeNull();
        warnings.ShouldBeEmpty();

        LegacyImportMapper.MatchDeveloper("Rahul", Developers, warnings).ShouldBeNull();
        warnings.ShouldHaveSingleItem().ShouldContain("matches 2 developers");

        warnings.Clear();
        LegacyImportMapper.MatchDeveloper("Emily / Amit", Developers, warnings)!.Id.ShouldBe(1);
        warnings.ShouldHaveSingleItem().ShouldContain("several people");

        warnings.Clear();
        LegacyImportMapper.MatchDeveloper("Priya", Developers, warnings).ShouldBeNull();
        warnings.ShouldHaveSingleItem().ShouldContain("matches no active developer");
    }

    [Fact]
    public void Status_Invalid_BeatsComplete_BeatsPending_BeatsAssignment()
    {
        static IssueStatus StatusOf(string? validity, string? complete, string? pending, string? resolveBy) =>
            Map(Raw(validity: validity, isComplete: complete, pending: pending, resolveBy: resolveBy)).Rows[0].Status;

        StatusOf("Invalid", "Yes", "Later", "Emily").ShouldBe(IssueStatus.Rejected);
        StatusOf("Valid", "yes", "Later", "Emily").ShouldBe(IssueStatus.Verified);
        StatusOf(null, "No", "Waiting for data", "Emily").ShouldBe(IssueStatus.OnHold);
        StatusOf(null, "No", null, "Emily").ShouldBe(IssueStatus.Assigned);
        StatusOf(null, null, null, null).ShouldBe(IssueStatus.New);
    }

    [Fact]
    public void MissingActualResult_UsesTheIssueText_WithAWarning_AndLongTitlesAreShortened()
    {
        var row = Map(Raw(actual: null, issue: "Open the invoice", title: new string('x', 250))).Rows[0];

        row.ActualResult.ShouldBe("Open the invoice");
        row.Title.Length.ShouldBe(LegacyImportMapper.TitleMaxLength);
        row.Warnings.ShouldContain(w => w.StartsWith("No Actual Result", StringComparison.Ordinal));
        row.Warnings.ShouldContain(w => w.StartsWith("Title shortened", StringComparison.Ordinal));
    }

    [Fact]
    public void Sheets_MatchExistingModulesIgnoringCase_AndEmptySheetsAreLeftOut()
    {
        var workbook = new RawWorkbook([
            new RawSheet("  Sales   &  Distribution ", [Raw()], []),
            new RawSheet("Notes", [], []),
            new RawSheet("Item Mast, BOM, Tax Str.Mast", [Raw()], []),
        ]);

        var (modules, rows) = LegacyImportMapper.Map(workbook, [new ImportModuleRef(9, "SALES & DISTRIBUTION")], Developers);

        modules.Select(m => (m.SheetIndex, m.Name, m.ExistingModuleId)).ShouldBe([(0, "SALES & DISTRIBUTION", (int?)9), (2, "Item Mast, BOM, Tax Str.Mast", null)]);
        rows.Select(r => r.Id).ShouldBe(["1-3", "3-3"]);
    }

    [Fact]
    public void Corrections_ReplaceValues_AndClearTheWarningsTheySettle()
    {
        var row = Map(Raw(severity: "Not yet assessed", priority: "Pending", resolveBy: "Priya")).Rows[0];
        row.Warnings.Count.ShouldBe(3);

        var fixedRow = LegacyImportMapper.Apply(row, new RowCorrection(row.Id, Severity: Severity.High, Priority: Priority.Low, AssigneeId: 4), Developers);

        (fixedRow.Severity, fixedRow.Priority, fixedRow.AssigneeId, fixedRow.Status).ShouldBe((Severity.High, Priority.Low, (int?)4, IssueStatus.Assigned));
        fixedRow.Warnings.ShouldBeEmpty();

        var unassigned = LegacyImportMapper.Apply(fixedRow, new RowCorrection(row.Id, Unassign: true), Developers);
        (unassigned.AssigneeId, unassigned.Status).ShouldBe(((int?)null, IssueStatus.New));
    }

    [Fact]
    public void PlainText_BecomesEscapedParagraphs()
    {
        LegacyImportMapper.ToHtml("Shows 0\n\n<script>x</script> & more").ShouldBe("<p>Shows 0</p><p>&lt;script&gt;x&lt;/script&gt; &amp; more</p>");
        LegacyImportMapper.ToHtml("  ").ShouldBeNull();
    }

    private static (IReadOnlyList<ImportModule> Modules, IReadOnlyList<ImportRow> Rows) Map(RawIssue issue) =>
        LegacyImportMapper.Map(new RawWorkbook([new RawSheet("Sales", [issue], [])]), [], Developers);

    private static RawIssue Raw(
        string title = "Invoice total is wrong",
        string? issue = null,
        string? actual = "Shows 0",
        string? severity = "High",
        string? priority = "Medium",
        string? resolveBy = null,
        string? isComplete = null,
        string? pending = null,
        string? validity = null) =>
        new(3, 3, title, null, issue, actual, null, null, severity, priority, resolveBy, isComplete, pending, validity, [], []);
}
