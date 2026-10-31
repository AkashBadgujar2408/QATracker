using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using QaTracker.Application.Features.Analytics;
using QaTracker.Application.Features.Reports;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Infrastructure.Reports;

/// <summary>
/// Excel files with ClosedXML (MIT). The sprint report has a summary sheet, one sheet per module laid out like the
/// legacy testing workbook (numbered issue block, severity | priority, screenshots, resolve by, is complete, pending
/// reason, valid / invalid) and an "All issues" table.
/// </summary>
internal static class ExcelReportWriter
{
    private const double ImageHeight = 110;
    private const double ImageGap = 6;
    private const int ScreenshotColumn = 4;
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");
    private static readonly XLColor Ink = XLColor.FromHtml("#0F172A");
    private static readonly XLColor Muted = XLColor.FromHtml("#64748B");

    public static byte[] Sprint(SprintReport report)
    {
        using var book = new XLWorkbook();
        Summary(book.AddWorksheet("Summary"), report);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Summary", "All issues" };
        foreach (var module in report.Issues.GroupBy(i => i.Module).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            ModuleSheet(book.AddWorksheet(SheetName(module.Key, names)), module.Key, module.ToList());
        }

        AllIssues(book.AddWorksheet("All issues"), report);
        return Save(book);
    }

    public static byte[] IssueList(IssueExport export)
    {
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("Issues");
        ws.Cell(1, 1).Value = "Issues";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"{export.Filters} · {export.Rows.Count} issues · exported {ReportText.DateTime(export.GeneratedAt)}";
        ws.Cell(2, 1).Style.Font.SetFontColor(Muted);

        string[] headers = ["Key", "Title", "Client", "Project", "Sprint", "Module", "Screen", "Type", "Severity", "Priority", "Status", "Assignee", "Reporter", "Found on", "Due", "Created", "Updated", "Reopened"];
        var row = 4;
        HeaderRow(ws, row, headers);
        foreach (var i in export.Rows)
        {
            row++;
            Values(ws, row,
                i.Key, i.Title, i.Client, i.Project, i.Sprint, i.Module, i.Screen ?? "", i.Type.ToString(), ReportText.Severity(i.Severity),
                i.Priority.ToString(), ReportText.Status(i.Status), i.Assignee ?? "", i.Reporter, i.Environment ?? "");
            Dates(ws, row, 15, i.DueDate, i.CreatedAt, i.UpdatedAt);
            ws.Cell(row, 18).Value = i.ReopenCount;
        }

        Table(ws, 4, row, headers.Length);
        ws.Column(2).Width = 60;
        return Save(book);
    }

    private static void Summary(IXLWorksheet ws, SprintReport report)
    {
        var h = report.Header;
        ws.Cell(1, 1).Value = "Sprint report";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(Ink);

        (string Label, string Value)[] facts =
        [
            ("Client", h.ClientName),
            ("Project", $"{h.ProjectKey} – {h.ProjectName}"),
            ("Sprint", h.SprintName),
            ("Phase", h.PhaseName),
            ("Status", ReportText.SprintStatus(h.Status)),
            ("Started", ReportText.Date(h.StartedAt)),
            ("Testing completed", ReportText.Date(h.TestingCompletedAt)),
            ("Closed", ReportText.Date(h.ClosedAt)),
            ("Carried forward", report.Summary.CarriedForward > 0 ? $"{report.Summary.CarriedForward} issue(s), shown as they were when they left" : "None"),
            ("Generated", $"{ReportText.DateTime(h.GeneratedAt)} by {h.GeneratedBy}"),
        ];
        var row = 3;
        foreach (var (label, value) in facts)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.SetFontColor(Muted);
            ws.Cell(row, 2).Value = value;
            row++;
        }

        var s = report.Summary;
        row += 1;
        row = CountTable(ws, row, "By status", Enum.GetValues<StatusGroup>().Select(g => (g.ToString(), s.ByGroup[g])), s.Total);
        row += 1;
        row = CountTable(ws, row, "By severity", s.BySeverity.Select(p => (ReportText.Severity(p.Key), p.Value)), s.Total);

        row += 1;
        ws.Cell(row, 1).Value = "By module";
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        var groups = Enum.GetValues<StatusGroup>();
        var severities = s.BySeverity.Keys.ToList();
        string[] headers = ["Module", "Total", .. groups.Select(g => g.ToString()), .. severities.Select(ReportText.Severity)];
        var first = row;
        HeaderRow(ws, row, headers);
        foreach (var m in s.ByModule)
        {
            row++;
            ws.Cell(row, 1).Value = m.Module;
            ws.Cell(row, 2).Value = m.Total;
            var c = 3;
            foreach (var g in groups)
            {
                ws.Cell(row, c++).Value = m.ByGroup[g];
            }

            foreach (var sev in severities)
            {
                ws.Cell(row, c++).Value = m.BySeverity[sev];
            }
        }

        Borders(ws.Range(first, 1, row, headers.Length));
        ws.Column(1).Width = 24;
        ws.Column(2).Width = 34;
        ws.Columns(3, headers.Length).Width = 12;
        ws.ShowGridLines = false;
    }

    private static int CountTable(IXLWorksheet ws, int row, string title, IEnumerable<(string Label, int Count)> counts, int total)
    {
        ws.Cell(row, 1).Value = title;
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        var first = row;
        HeaderRow(ws, row, ["", "Issues"]);
        foreach (var (label, count) in counts)
        {
            row++;
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 2).Value = count;
        }

        row++;
        ws.Cell(row, 1).Value = "Total";
        ws.Cell(row, 2).Value = total;
        ws.Range(row, 1, row, 2).Style.Font.SetBold();
        Borders(ws.Range(first, 1, row, 2));
        ws.Range(first, 2, row, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        return row + 1;
    }

    /// <summary>One module like the legacy workbook: the issue block in column B and the status columns filled.</summary>
    private static void ModuleSheet(IXLWorksheet ws, string module, IReadOnlyList<SprintReportIssue> issues)
    {
        ws.Cell(1, 1).Value = module;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        string[] headers = ["#", "Issue", "Severity | Priority", "Screenshots", "Resolve By", "Status", "Is Complete", "Pending Reason", "Valid / Invalid", "Key", "Due"];
        const int headerRow = 3;
        HeaderRow(ws, headerRow, headers);

        var row = headerRow;
        var n = 0;
        foreach (var i in issues)
        {
            row++;
            n++;
            ws.Cell(row, 1).Value = n;
            ws.Cell(row, 2).Value = IssueBlock(n, i);
            ws.Cell(row, 3).Value = $"Severity: {ReportText.Severity(i.Severity)}\nPriority: {i.Priority}";
            ws.Cell(row, 5).Value = i.Assignee ?? "";
            ws.Cell(row, 6).Value = ReportText.Status(i.Status) + (i.CarriedForwardTo is { } to ? $"\nCarried forward to {to}" : "");
            ws.Cell(row, 7).Value = i.Status is IssueStatus.Verified or IssueStatus.Closed ? "Yes" : "No";
            ws.Cell(row, 8).Value = i.Status == IssueStatus.OnHold ? i.HoldReason ?? "" : "";
            ws.Cell(row, 9).Value = i.Status == IssueStatus.Rejected ? ReportText.Reject(i.RejectType) : "Valid";
            ws.Cell(row, 10).Value = i.Key;
            ws.Cell(row, 11).Value = ReportText.Date(i.DueDate);

            var height = Pictures(ws, row, i.Thumbnails);
            var lines = ws.Cell(row, 2).GetString().Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length / 70d)));
            ws.Row(row).Height = Math.Min(409, Math.Max(height * 0.75 + 8, lines * 15 + 6));
        }

        var range = ws.Range(headerRow, 1, row, headers.Length);
        Borders(range);
        range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top).Alignment.SetWrapText();
        ws.Column(1).Width = 5;
        ws.Column(2).Width = 75;
        ws.Column(3).Width = 18;
        ws.Column(ScreenshotColumn).Width = 50;
        ws.Columns(5, 9).Width = 15;
        ws.Column(10).Width = 12;
        ws.Column(11).Width = 13;
        ws.SheetView.FreezeRows(headerRow);
    }

    private static string IssueBlock(int n, SprintReportIssue i)
    {
        var parts = new List<string> { $"{n}. {i.Title}" };
        if (!string.IsNullOrWhiteSpace(i.Screen))
        {
            parts.Add($"Screen: {i.Screen}");
        }

        if (i.Steps is { } steps)
        {
            parts.Add($"Issue: {steps}");
        }

        parts.Add($"Actual Result: {i.ActualResult}");
        if (i.ExpectedResult is { } expected)
        {
            parts.Add($"Expected Result: {expected}");
        }

        if (i.Impact is { } impact)
        {
            parts.Add($"Impact: {impact}");
        }

        return string.Join('\n', parts);
    }

    /// <summary>Thumbnails side by side in the screenshot cell; returns the height they need in pixels.</summary>
    private static double Pictures(IXLWorksheet ws, int row, IReadOnlyList<byte[]> thumbnails)
    {
        var x = 4d;
        var placed = false;
        foreach (var thumbnail in thumbnails)
        {
            if (ReportImages.ToPng(thumbnail) is not { } png)
            {
                continue;
            }

            using var stream = new MemoryStream(png.Bytes);
            var scale = ImageHeight / png.Height;
            var width = png.Width * scale;
            ws.AddPicture(stream, XLPictureFormat.Png)
                .MoveTo(ws.Cell(row, ScreenshotColumn), (int)x, 4)
                .WithSize((int)width, (int)ImageHeight);
            x += width + ImageGap;
            placed = true;
        }

        return placed ? ImageHeight + 8 : 0;
    }

    private static void AllIssues(IXLWorksheet ws, SprintReport report)
    {
        string[] headers = ["Key", "Module", "Screen", "Type", "Title", "Severity", "Priority", "Status", "Assignee", "Reporter", "Found on", "Created", "Due", "First fixed", "Closed", "Reopened", "Reject reason", "Carried forward to"];
        HeaderRow(ws, 1, headers);
        var row = 1;
        foreach (var i in report.Issues)
        {
            row++;
            Values(ws, row,
                i.Key, i.Module, i.Screen ?? "", i.Type.ToString(), i.Title, ReportText.Severity(i.Severity), i.Priority.ToString(),
                ReportText.Status(i.Status), i.Assignee ?? "", i.Reporter, i.Environment ?? "");
            Dates(ws, row, 12, i.CreatedAt, i.DueDate, i.FirstFixedAt, i.ClosedAt);
            ws.Cell(row, 16).Value = i.ReopenCount;
            ws.Cell(row, 17).Value = i.Status == IssueStatus.Rejected
                ? string.Join(" – ", new[] { ReportText.Reject(i.RejectType), i.RejectReason }.Where(t => !string.IsNullOrWhiteSpace(t)))
                : "";
            ws.Cell(row, 18).Value = i.CarriedForwardTo ?? "";
        }

        Table(ws, 1, row, headers.Length);
        ws.Column(5).Width = 60;
    }

    private static void HeaderRow(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
    {
        for (var c = 0; c < headers.Count; c++)
        {
            ws.Cell(row, c + 1).Value = headers[c];
        }

        var range = ws.Range(row, 1, row, headers.Count);
        range.Style.Font.SetBold().Fill.SetBackgroundColor(HeaderFill);
    }

    private static void Values(IXLWorksheet ws, int row, params string[] values)
    {
        for (var c = 0; c < values.Length; c++)
        {
            ws.Cell(row, c + 1).Value = values[c];
        }
    }

    /// <summary>IST dates as real Excel dates shown 27/Oct/2026, so they sort and filter as dates.</summary>
    private static void Dates(IXLWorksheet ws, int row, int firstColumn, params DateTimeOffset?[] values)
    {
        for (var c = 0; c < values.Length; c++)
        {
            if (values[c] is not { } value)
            {
                continue;
            }

            var cell = ws.Cell(row, firstColumn + c);
            cell.Value = value.ToOffset(WorkingCalendar.Ist).Date;
            cell.Style.DateFormat.Format = "dd/mmm/yyyy";
        }
    }

    /// <summary>Header + rows as a filterable block: frozen header, auto filter, sized columns.</summary>
    private static void Table(IXLWorksheet ws, int headerRow, int lastRow, int columns)
    {
        var range = ws.Range(headerRow, 1, Math.Max(headerRow, lastRow), columns);
        Borders(range);
        range.SetAutoFilter();
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns(1, columns).AdjustToContents(headerRow, Math.Min(lastRow, headerRow + 500), 8, 40);
    }

    private static void Borders(IXLRange range)
    {
        range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);
        range.Style.Border.SetOutsideBorderColor(XLColor.FromHtml("#CBD5E1")).Border.SetInsideBorderColor(XLColor.FromHtml("#CBD5E1"));
    }

    /// <summary>Excel sheet names: at most 31 characters, none of : \ / ? * [ ], unique (case-insensitive).</summary>
    internal static string SheetName(string name, ISet<string> used)
    {
        var clean = new string(name.Select(ch => ":\\/?*[]".Contains(ch) ? ' ' : ch).ToArray()).Trim().Trim('\'');
        if (clean.Length == 0)
        {
            clean = "Module";
        }

        clean = clean.Length > 31 ? clean[..31].TrimEnd() : clean;
        var candidate = clean;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
        }

        return candidate;
    }

    private static byte[] Save(XLWorkbook book)
    {
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }
}
