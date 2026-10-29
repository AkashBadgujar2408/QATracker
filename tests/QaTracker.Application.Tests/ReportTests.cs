using QaTracker.Application.Features.Analytics;
using QaTracker.Application.Features.Reports;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Tests;

public sealed class ReportTests
{
    [Fact]
    public void FromHtml_KeepsParagraphsAndListItemsAsLines()
    {
        ReportText.FromHtml("<p>Open the <b>invoice</b>&nbsp;screen</p><ul><li>Add a line</li><li>Save</li></ul><p><br></p>")
            .ShouldBe("Open the invoice screen\n• Add a line\n• Save");
        ReportText.FromHtml("<p> </p>").ShouldBeNull();
        ReportText.FromHtml(null).ShouldBeNull();
    }

    [Fact]
    public void Dates_AreIst_InTheAppFormat()
    {
        var lateEvening = new DateTimeOffset(2026, 10, 27, 20, 0, 0, TimeSpan.Zero);

        ReportText.Date(lateEvening).ShouldBe("28/Oct/2026");
        ReportText.DateTime(lateEvening).ShouldBe("28/Oct/2026 01:30");
        ReportText.Date(new DateTimeOffset(2026, 9, 5, 6, 0, 0, TimeSpan.Zero)).ShouldBe("05/Sep/2026");
        ReportText.Date((DateTimeOffset?)null).ShouldBe("");
    }

    [Fact]
    public void FileNames_DropCharactersWindowsRejects()
    {
        ReportNames.File("PGERP Sprint 2 – UAT report 27/Oct/2026").ShouldBe("PGERP Sprint 2 – UAT report 27-Oct-2026");
        ReportNames.File("a:b*c?  \"d\"<e>|f.").ShouldBe("a-b-c- -d--e--f");
    }

    [Fact]
    public void Summary_CountsGroupsSeveritiesAndModules()
    {
        var report = new SprintReport(Header(), [
            Issue("Sales", IssueStatus.New, Severity.High),
            Issue("Sales", IssueStatus.Reopened, Severity.Critical),
            Issue("billing", IssueStatus.Closed, Severity.High),
            Issue("Sales", IssueStatus.Rejected, Severity.NotAssessed),
        ]);

        var s = report.Summary;
        s.Total.ShouldBe(4);
        s.ByGroup[StatusGroup.Open].ShouldBe(2);
        s.ByGroup[StatusGroup.Closed].ShouldBe(1);
        s.ByGroup[StatusGroup.Rejected].ShouldBe(1);
        s.BySeverity.Keys.ShouldBe([Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.NotAssessed]);
        s.BySeverity[Severity.High].ShouldBe(2);
        s.ByModule.Select(m => (m.Module, m.Total)).ShouldBe([("billing", 1), ("Sales", 3)]);
        s.ByModule[1].ByGroup[StatusGroup.Open].ShouldBe(2);
    }

    private static SprintReportHeader Header() => new(
        1, "Sprint 1", "UAT", SprintStatus.Testing, "Acme", "PG", "ERP",
        DateTimeOffset.UnixEpoch, null, null, DateTimeOffset.UnixEpoch, "John Tester");

    private static SprintReportIssue Issue(string module, IssueStatus status, Severity severity) => new(
        "PG-1", 1, module, null, IssueType.Bug, "Title", null, "Actual", null, null, severity, Priority.Medium, status,
        null, "John Tester", null, DateTimeOffset.UnixEpoch, null, null, null, 0, null, null, null, []);
}
