using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using QaTracker.Application.Features.Import;

namespace QaTracker.Infrastructure.Import;

/// <summary>
/// Reads the legacy "Testing Sheet" workbook with ClosedXML (Appendix C). Per visible sheet: the header row (the
/// first of the top rows naming at least two known columns: Issue, Severity, Resolve By, Is Complete, Pending Reason,
/// Valid/Invalid …), the issue column (header "Issue"/"Description", else column B), issue blocks starting with
/// "N. Title", labelled texts inside a block, and floating pictures anchored to the block's rows.
/// </summary>
public sealed partial class ClosedXmlLegacyWorkbookParser : ILegacyWorkbookParser
{
    private const int HeaderSearchRows = 15;
    private const int DefaultIssueColumn = 2;

    private enum Column
    {
        Issue,
        Severity,
        Priority,
        SeverityPriority,
        ResolveBy,
        IsComplete,
        PendingReason,
        Validity,
        Serial,
    }

    private static readonly IReadOnlyDictionary<string, Column> Headers = new Dictionary<string, Column>
    {
        ["issue"] = Column.Issue,
        ["issues"] = Column.Issue,
        ["description"] = Column.Issue,
        ["issuedescription"] = Column.Issue,
        ["observation"] = Column.Issue,
        ["observations"] = Column.Issue,
        ["bug"] = Column.Issue,
        ["bugs"] = Column.Issue,
        ["severity"] = Column.Severity,
        ["priority"] = Column.Priority,
        ["severitypriority"] = Column.SeverityPriority,
        ["resolveby"] = Column.ResolveBy,
        ["resolvedby"] = Column.ResolveBy,
        ["assignedto"] = Column.ResolveBy,
        ["iscomplete"] = Column.IsComplete,
        ["iscompleted"] = Column.IsComplete,
        ["complete"] = Column.IsComplete,
        ["pendingreason"] = Column.PendingReason,
        ["reasonforpending"] = Column.PendingReason,
        ["validinvalid"] = Column.Validity,
        ["valid"] = Column.Validity,
        ["validity"] = Column.Validity,
        ["srno"] = Column.Serial,
        ["sno"] = Column.Serial,
        ["no"] = Column.Serial,
        ["sr"] = Column.Serial,
    };

    public RawWorkbook? Parse(byte[] workbook)
    {
        XLWorkbook book;
        try
        {
            book = new XLWorkbook(new MemoryStream(workbook));
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or IOException or ArgumentException or InvalidOperationException or NotSupportedException
                                      || e.GetType().Namespace?.StartsWith("DocumentFormat.OpenXml", StringComparison.Ordinal) == true)
        {
            return null;
        }

        using (book)
        {
            return new RawWorkbook(book.Worksheets
                .Where(ws => ws.Visibility == XLWorksheetVisibility.Visible)
                .Select(Sheet)
                .ToList());
        }
    }

    private static RawSheet Sheet(IXLWorksheet ws)
    {
        var warnings = new List<string>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0)
        {
            return new RawSheet(ws.Name, [], []);
        }

        var (headerRow, columns) = FindHeader(ws, lastRow, lastColumn);
        if (headerRow == 0)
        {
            warnings.Add("No header row (Resolve By, Is Complete …) found – the issue text is read from column B and the status columns are not filled.");
        }

        var issueColumn = columns.FirstOrDefault(c => c.Value == Column.Issue).Key;
        if (issueColumn == 0)
        {
            issueColumn = DefaultIssueColumn;
        }

        var starts = new List<(int Row, string Title, string Remainder)>();
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var text = Text(ws.Cell(row, issueColumn));
            var lines = text.Split('\n');
            var match = IssueStart().Match(lines[0]);
            if (match.Success && !Label().IsMatch(lines[0]))
            {
                starts.Add((row, match.Groups[1].Value.Trim(), string.Join('\n', lines.Skip(1))));
            }
        }

        var pictures = ws.Pictures
            .Select(p => (Picture: p, Row: p.TopLeftCell.Address.RowNumber, Col: p.TopLeftCell.Address.ColumnNumber))
            .OrderBy(p => p.Row).ThenBy(p => p.Col)
            .ToList();
        if (starts.Count > 0 && pictures.Any(p => p.Row < starts[0].Row))
        {
            warnings.Add("Pictures above the first issue were ignored.");
        }

        var issues = new List<RawIssue>();
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1].Row - 1 : lastRow;
            if (i + 1 == starts.Count)
            {
                // The last block also owns pictures below its text.
                end = Math.Max(end, pictures.Select(p => p.Row).DefaultIfEmpty(end).Max());
            }

            issues.Add(Block(ws, start.Row, end, start.Title, start.Remainder, issueColumn, columns, lastColumn,
                pictures.Where(p => p.Row >= start.Row && p.Row <= end).Select(p => p.Picture).ToList()));
        }

        return new RawSheet(ws.Name, issues, warnings);
    }

    private static (int Row, Dictionary<int, Column> Columns) FindHeader(IXLWorksheet ws, int lastRow, int lastColumn)
    {
        for (var row = 1; row <= Math.Min(lastRow, HeaderSearchRows); row++)
        {
            var columns = new Dictionary<int, Column>();
            for (var col = 1; col <= lastColumn; col++)
            {
                if (Headers.TryGetValue(Key(Text(ws.Cell(row, col))), out var column) && !columns.ContainsValue(column))
                {
                    columns[col] = column;
                }
            }

            if (columns.Count(c => c.Value != Column.Serial) >= 2)
            {
                return (row, columns);
            }
        }

        return (0, []);
    }

    private static RawIssue Block(
        IXLWorksheet ws,
        int first,
        int last,
        string title,
        string firstCellRest,
        int issueColumn,
        IReadOnlyDictionary<int, Column> columns,
        int lastColumn,
        IReadOnlyList<IXLPicture> pictures)
    {
        // The issue column of the whole block, read as one text.
        var lines = new List<string>();
        if (firstCellRest.Length > 0)
        {
            lines.AddRange(firstCellRest.Split('\n'));
        }

        for (var row = first + 1; row <= last; row++)
        {
            var text = Text(ws.Cell(row, issueColumn));
            if (text.Length > 0)
            {
                lines.AddRange(text.Split('\n'));
            }
        }

        string? severity = null, priority = null, resolveBy = null, isComplete = null, pending = null, validity = null;

        // "Severity: X" / "Priority: Y" written inside the issue text.
        lines.RemoveAll(line =>
        {
            var sev = SeverityLabel().Match(line);
            var pri = PriorityLabel().Match(line);
            severity ??= sev.Success ? sev.Groups[1].Value.Trim() : null;
            priority ??= pri.Success ? pri.Groups[1].Value.Trim() : null;
            return (sev.Success || pri.Success) && SeverityPriorityLine().IsMatch(line);
        });

        var sections = Sections(lines);
        var others = new List<string>();
        for (var row = first; row <= last; row++)
        {
            for (var col = 1; col <= lastColumn; col++)
            {
                if (col == issueColumn)
                {
                    continue;
                }

                var text = Text(ws.Cell(row, col));
                if (text.Length == 0)
                {
                    continue;
                }

                var sev = SeverityLabel().Match(text);
                var pri = PriorityLabel().Match(text);
                if (sev.Success || pri.Success)
                {
                    severity ??= sev.Success ? sev.Groups[1].Value.Trim() : null;
                    priority ??= pri.Success ? pri.Groups[1].Value.Trim() : null;
                    continue;
                }

                switch (columns.TryGetValue(col, out var column) ? column : (Column?)null)
                {
                    case Column.Severity:
                        severity ??= text;
                        break;
                    case Column.Priority:
                        priority ??= text;
                        break;
                    case Column.ResolveBy:
                        resolveBy ??= text;
                        break;
                    case Column.IsComplete:
                        isComplete ??= text;
                        break;
                    case Column.PendingReason:
                        pending ??= text;
                        break;
                    case Column.Validity:
                        validity ??= text;
                        break;
                    case Column.Serial or Column.SeverityPriority:
                        break;
                    default:
                        if (!ImageLabel().IsMatch(text) && !Number().IsMatch(text) && !others.Contains(text))
                        {
                            others.Add(text);
                        }

                        break;
                }
            }
        }

        var images = new List<RawImage>();
        foreach (var picture in pictures)
        {
            using var copy = new MemoryStream();
            picture.ImageStream.Position = 0;
            picture.ImageStream.CopyTo(copy);
            images.Add(new RawImage($"{ws.Name}-row{first}-{images.Count + 1}{Extension(picture.Format)}", copy.ToArray()));
        }

        return new RawIssue(
            first,
            last,
            title,
            sections.GetValueOrDefault(""),
            sections.GetValueOrDefault("issue"),
            sections.GetValueOrDefault("actual"),
            sections.GetValueOrDefault("expected"),
            sections.GetValueOrDefault("impact"),
            severity,
            priority,
            resolveBy,
            isComplete,
            pending,
            validity,
            others,
            images);
    }

    /// <summary>Lines split by "Issue:", "Actual Result:", "Expected Result:", "Impact:" (text may follow the label or the next lines).</summary>
    private static Dictionary<string, string> Sections(IEnumerable<string> lines)
    {
        var sections = new Dictionary<string, List<string>>();
        var current = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var label = Label().Match(line);
            if (label.Success)
            {
                current = SectionOf(label.Groups[1].Value);
                line = label.Groups[2].Value.Trim();
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (!sections.TryGetValue(current, out var list))
            {
                sections[current] = list = [];
            }

            list.Add(line);
        }

        return sections.ToDictionary(s => s.Key, s => string.Join('\n', s.Value));
    }

    private static string SectionOf(string label)
    {
        var key = Key(label);
        return key.StartsWith("actual", StringComparison.Ordinal) ? "actual"
            : key.StartsWith("expected", StringComparison.Ordinal) ? "expected"
            : key.StartsWith("impact", StringComparison.Ordinal) ? "impact"
            : "issue";
    }

    /// <summary>The cell as text with line breaks; "&lt;br&gt;" written as text also breaks lines.</summary>
    private static string Text(IXLCell cell)
    {
        var text = cell.GetFormattedString();
        text = BreakTag().Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), "\n");
        return text.Trim();
    }

    private static string Key(string header) => NonLetters().Replace(header.ToLowerInvariant(), "");

    private static string Extension(XLPictureFormat format) => format switch
    {
        XLPictureFormat.Jpeg => ".jpg",
        XLPictureFormat.Gif => ".gif",
        XLPictureFormat.Bmp => ".bmp",
        XLPictureFormat.Tiff => ".tiff",
        _ => ".png",
    };

    [GeneratedRegex(@"^\s*\d+\s*[.)]\s*(.+)$")]
    private static partial Regex IssueStart();

    [GeneratedRegex(@"^\s*(issue\s*description|issue|description|steps(?:\s*to\s*reproduce)?|actual\s*result|actual|expected\s*result|expected|impact)\s*[:\-–]\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Label();

    [GeneratedRegex(@"severity\s*[:\-–]\s*([^\n|,;]+?)(?=\s*(?:[|,;]|priority\b|$))", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SeverityLabel();

    [GeneratedRegex(@"priority\s*[:\-–]\s*([^\n|,;]+?)(?=\s*(?:[|,;]|severity\b|$))", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PriorityLabel();

    /// <summary>A line that is only severity / priority labels ("Severity: High", "Severity: High | Priority: Low").</summary>
    [GeneratedRegex(@"^\s*(severity|priority)\s*[:\-–]", RegexOptions.IgnoreCase)]
    private static partial Regex SeverityPriorityLine();

    [GeneratedRegex(@"^img\s*[a-z0-9]{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex ImageLabel();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex Number();

    [GeneratedRegex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();

    [GeneratedRegex("[^a-z]")]
    private static partial Regex NonLetters();
}
