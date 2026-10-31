using QaTracker.Application.Features.Analytics;
using QaTracker.Application.Features.Reports;
using QaTracker.Domain.Enums;
using SkiaSharp;

namespace QaTracker.Infrastructure.Reports;

/// <summary>
/// Sprint report as PDF with SkiaSharp's PDF backend (MIT, already used for thumbnails – ADR-035): cover, summary
/// tables with bars, and the issue list grouped by module with up to three screenshot thumbnails per issue.
/// A4 portrait, sizes in points.
/// </summary>
internal sealed class PdfReportWriter : IDisposable
{
    private const float PageWidth = 595;
    private const float PageHeight = 842;
    private const float Margin = 40;
    private const float ContentWidth = PageWidth - 2 * Margin;
    private const float FooterSpace = 28;
    private const float ThumbnailHeight = 90;

    private static readonly SKColor Ink = SKColor.Parse("#0F172A");
    private static readonly SKColor Text = SKColor.Parse("#1E293B");
    private static readonly SKColor Muted = SKColor.Parse("#64748B");
    private static readonly SKColor Rule = SKColor.Parse("#E2E8F0");
    private static readonly SKColor Primary = SKColor.Parse("#4F46E5");
    private static readonly SKColor HeaderFill = SKColor.Parse("#EEF2FF");

    private static readonly IReadOnlyDictionary<StatusGroup, SKColor> GroupColors = new Dictionary<StatusGroup, SKColor>
    {
        [StatusGroup.Open] = SKColor.Parse("#3B82F6"),
        [StatusGroup.Fixed] = SKColor.Parse("#8B5CF6"),
        [StatusGroup.Verified] = SKColor.Parse("#22C55E"),
        [StatusGroup.Closed] = SKColor.Parse("#15803D"),
        [StatusGroup.Rejected] = SKColor.Parse("#94A3B8"),
    };

    private static readonly IReadOnlyDictionary<Severity, SKColor> SeverityColors = new Dictionary<Severity, SKColor>
    {
        [Severity.Critical] = SKColor.Parse("#DC2626"),
        [Severity.High] = SKColor.Parse("#EA580C"),
        [Severity.Medium] = SKColor.Parse("#D97706"),
        [Severity.Low] = SKColor.Parse("#2563EB"),
        [Severity.NotAssessed] = SKColor.Parse("#94A3B8"),
    };

    private readonly MemoryStream _output = new();
    private readonly SKDocument _document;
    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly string _footer;
    private SKCanvas? _canvas;
    private float _y;
    private int _page;
    private bool _pageHasFooter;

    private PdfReportWriter(SprintReport report)
    {
        var h = report.Header;
        _footer = $"{h.ProjectKey} · {h.SprintName} · generated {ReportText.DateTime(h.GeneratedAt)}";
        _regular = Typeface(SKFontStyle.Normal);
        _bold = Typeface(SKFontStyle.Bold);
        _document = SKDocument.CreatePdf(_output, new SKDocumentPdfMetadata
        {
            Title = $"{h.ProjectKey} {h.SprintName} – sprint report",
            Author = h.GeneratedBy,
            Creator = "QA Tracker",
            Producer = "QA Tracker",
            Creation = h.GeneratedAt.UtcDateTime,
            Modified = h.GeneratedAt.UtcDateTime,
        }) ?? throw new InvalidOperationException("The PDF backend of SkiaSharp is not available.");
    }

    public static byte[] Sprint(SprintReport report)
    {
        using var writer = new PdfReportWriter(report);
        writer.Cover(report);
        writer.SummaryPage(report);
        writer.IssuePages(report);
        return writer.Finish();
    }

    public void Dispose()
    {
        _document.Dispose();
        _regular.Dispose();
        _bold.Dispose();
        _output.Dispose();
    }

    /// <summary>A UI font of the server (Segoe UI on Windows), else the platform default.</summary>
    private static SKTypeface Typeface(SKFontStyle style)
    {
        foreach (var family in new[] { "Segoe UI", "Inter", "Arial", "DejaVu Sans", "Liberation Sans" })
        {
            var typeface = SKTypeface.FromFamilyName(family, style);
            if (typeface is not null && typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                return typeface;
            }

            typeface?.Dispose();
        }

        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }

    private byte[] Finish()
    {
        EndPage();
        _document.Close();
        return _output.ToArray();
    }

    // ---- pages ----

    private void Cover(SprintReport report)
    {
        var h = report.Header;
        var canvas = NewPage(footer: false);
        using (var band = Fill(Ink))
        {
            canvas.DrawRect(0, 0, PageWidth, 250, band);
        }

        using var small = Font(_bold, 11);
        using var title = Font(_bold, 30);
        using var subtitle = Font(_regular, 14);
        DrawText("QA TRACKER", Margin, 70, small, SKColor.Parse("#A5B4FC"));
        DrawText("Sprint report", Margin, 120, title, SKColors.White);
        var y = 150f;
        foreach (var line in Wrap($"{h.SprintName} · {h.ProjectKey} – {h.ProjectName}", subtitle, ContentWidth))
        {
            DrawText(line, Margin, y, subtitle, SKColor.Parse("#CBD5E1"));
            y += 20;
        }

        _y = 300;
        (string, string)[] facts =
        [
            ("Client", h.ClientName),
            ("Project", $"{h.ProjectKey} – {h.ProjectName}"),
            ("Sprint", h.SprintName),
            ("Phase", h.PhaseName),
            ("Status", ReportText.SprintStatus(h.Status)),
            ("Started", ReportText.Date(h.StartedAt)),
            ("Testing completed", ReportText.Date(h.TestingCompletedAt) is { Length: > 0 } done ? done : "–"),
            ("Closed", ReportText.Date(h.ClosedAt) is { Length: > 0 } closed ? closed : "–"),
            ("Issues", report.Summary.Total.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (report.Summary.CarriedForward > 0 ? $" ({report.Summary.CarriedForward} carried forward, as they were when they left)" : "")),
            ("Generated", $"{ReportText.DateTime(h.GeneratedAt)} by {h.GeneratedBy}"),
        ];
        using var label = Font(_regular, 11);
        using var value = Font(_bold, 12);
        foreach (var (name, text) in facts)
        {
            DrawText(name, Margin, _y, label, Muted);
            var lines = Wrap(text, value, ContentWidth - 140);
            foreach (var line in lines)
            {
                DrawText(line, Margin + 140, _y, value, Text);
                _y += 17;
            }

            _y += 9;
        }

        if (h.Status == SprintStatus.Closed)
        {
            using var note = Font(_regular, 10);
            DrawText("Closed sprint: status, severity and assignee as frozen at closing.", Margin, _y + 10, note, Muted);
        }
    }

    private void SummaryPage(SprintReport report)
    {
        var s = report.Summary;
        NewPage();
        Heading("Summary");

        BarTable("By status", Enum.GetValues<StatusGroup>().Select(g => (g.ToString(), s.ByGroup[g], GroupColors[g])).ToList(), s.Total);
        BarTable("By severity", s.BySeverity.Select(p => (ReportText.Severity(p.Key), p.Value, SeverityColors[p.Key])).ToList(), s.Total);

        SubHeading("By module");
        var groups = Enum.GetValues<StatusGroup>();
        string[] headers = ["Module", "Total", .. groups.Select(g => g.ToString())];
        float[] widths = [ContentWidth - 6 * 58, 58, 58, 58, 58, 58, 58];
        var rows = s.ByModule
            .Select(m => (IReadOnlyList<string>)[m.Module, Number(m.Total), .. groups.Select(g => Number(m.ByGroup[g]))])
            .Append([ "Total", Number(s.Total), .. groups.Select(g => Number(s.ByGroup[g])) ])
            .ToList();
        Table(headers, widths, rows, boldLast: true);
    }

    private void IssuePages(SprintReport report)
    {
        NewPage();
        Heading("Issues");
        if (report.Issues.Count == 0)
        {
            using var font = Font(_regular, 11);
            DrawText("No issues were reported in this sprint.", Margin, _y, font, Muted);
            return;
        }

        foreach (var module in report.Issues.GroupBy(i => i.Module))
        {
            EnsureSpace(80);
            SubHeading($"{module.Key} ({module.Count()})");
            foreach (var issue in module)
            {
                IssueBlock(issue);
            }
        }
    }

    // ---- blocks ----

    private void IssueBlock(SprintReportIssue issue)
    {
        using var keyFont = Font(_bold, 11);
        using var titleFont = Font(_bold, 11);
        using var meta = Font(_regular, 9.5f);
        using var label = Font(_bold, 9.5f);
        using var body = Font(_regular, 9.5f);

        const float keyWidth = 78;
        const float chipWidth = 74;
        var titleLines = Wrap(issue.Title, titleFont, ContentWidth - keyWidth - chipWidth - 8, 3);
        var metaText = string.Join(" · ", new[]
        {
            issue.Screen,
            $"Priority {issue.Priority}",
            ReportText.Status(issue.Status),
            issue.CarriedForwardTo is { } to ? $"Carried forward to {to}" : null,
            issue.Assignee is { } a ? $"Assigned to {a}" : "Unassigned",
            issue.DueDate is { } due ? $"Due {ReportText.Date(due)}" : null,
            issue.Environment is { } env ? $"Found on {env}" : null,
            issue.ReopenCount > 0 ? $"Reopened {issue.ReopenCount}×" : null,
        }.Where(t => !string.IsNullOrWhiteSpace(t)));
        var metaLines = Wrap(metaText, meta, ContentWidth - keyWidth, 2);

        var sections = new List<(string Label, IReadOnlyList<string> Lines)>
        {
            ("Actual result", Wrap(issue.ActualResult, body, ContentWidth - keyWidth, 6)),
        };
        if (issue.ExpectedResult is { } expected)
        {
            sections.Add(("Expected result", Wrap(expected, body, ContentWidth - keyWidth, 4)));
        }

        if (issue.Status == IssueStatus.Rejected)
        {
            var reason = string.Join(" – ", new[] { ReportText.Reject(issue.RejectType), issue.RejectReason }.Where(t => !string.IsNullOrWhiteSpace(t)));
            sections.Add(("Rejected", Wrap(reason, body, ContentWidth - keyWidth, 3)));
        }
        else if (issue.Status == IssueStatus.OnHold && issue.HoldReason is { } hold)
        {
            sections.Add(("On hold", Wrap(hold, body, ContentWidth - keyWidth, 3)));
        }

        var images = issue.Thumbnails.Select(ReportImages.Decode).OfType<SKImage>().ToList();
        try
        {
            var height = titleLines.Count * 15 + metaLines.Count * 13 + 6
                + sections.Sum(s => 13 + s.Lines.Count * 12.5f + 4)
                + (images.Count > 0 ? ThumbnailHeight + 8 : 0) + 14;
            EnsureSpace(height);

            DrawText(issue.Key, Margin, _y, keyFont, Primary);
            Chip(ReportText.Severity(issue.Severity), SeverityColors[issue.Severity], PageWidth - Margin - chipWidth, _y - 10, chipWidth);
            foreach (var line in titleLines)
            {
                DrawText(line, Margin + keyWidth, _y, titleFont, Ink);
                _y += 15;
            }

            foreach (var line in metaLines)
            {
                DrawText(line, Margin + keyWidth, _y, meta, Muted);
                _y += 13;
            }

            _y += 6;
            foreach (var (name, lines) in sections)
            {
                DrawText(name, Margin + keyWidth, _y, label, Text);
                _y += 13;
                foreach (var line in lines)
                {
                    DrawText(line, Margin + keyWidth, _y, body, Text);
                    _y += 12.5f;
                }

                _y += 4;
            }

            if (images.Count > 0)
            {
                var x = Margin + keyWidth;
                using var border = Stroke(Rule, 0.75f);
                using var sampling = new SKPaint();
                foreach (var image in images)
                {
                    var width = Math.Min(image.Width * (ThumbnailHeight / image.Height), 200);
                    if (x + width > PageWidth - Margin)
                    {
                        break;
                    }

                    var rect = SKRect.Create(x, _y - 6, width, ThumbnailHeight);
                    _canvas!.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell), sampling);
                    _canvas.DrawRect(rect, border);
                    x += width + 8;
                }

                _y += ThumbnailHeight + 8;
            }

            using var rule = Stroke(Rule, 0.75f);
            _canvas!.DrawLine(Margin, _y, PageWidth - Margin, _y, rule);
            _y += 18;
        }
        finally
        {
            images.ForEach(i => i.Dispose());
        }
    }

    private void BarTable(string title, IReadOnlyList<(string Label, int Count, SKColor Color)> rows, int total)
    {
        SubHeading(title);
        using var font = Font(_regular, 10.5f);
        using var bold = Font(_bold, 10.5f);
        const float labelWidth = 110;
        const float countWidth = 50;
        var barWidth = ContentWidth - labelWidth - countWidth - 60;
        var max = Math.Max(1, rows.Max(r => r.Count));
        foreach (var (label, count, color) in rows)
        {
            EnsureSpace(18);
            DrawText(label, Margin, _y, font, Text);
            DrawText(Number(count), Margin + labelWidth + countWidth - 8, _y, bold, Ink, SKTextAlign.Right);
            if (count > 0)
            {
                using var fill = Fill(color);
                _canvas!.DrawRoundRect(SKRect.Create(Margin + labelWidth + countWidth, _y - 9, barWidth * count / max, 10), 2, 2, fill);
            }

            var share = total == 0 ? "" : $"{Math.Round(100d * count / total):0}%";
            DrawText(share, PageWidth - Margin, _y, font, Muted, SKTextAlign.Right);
            _y += 18;
        }

        _y += 14;
    }

    private void Table(IReadOnlyList<string> headers, IReadOnlyList<float> widths, IReadOnlyList<IReadOnlyList<string>> rows, bool boldLast)
    {
        using var font = Font(_regular, 10);
        using var bold = Font(_bold, 10);
        using var rule = Stroke(Rule, 0.75f);

        void Header()
        {
            using var fill = Fill(HeaderFill);
            _canvas!.DrawRect(Margin, _y - 12, ContentWidth, 18, fill);
            Row(headers, bold);
        }

        void Row(IReadOnlyList<string> cells, SKFont rowFont)
        {
            var x = Margin;
            for (var c = 0; c < cells.Count; c++)
            {
                var text = Fit(cells[c], rowFont, widths[c] - 8);
                if (c == 0)
                {
                    DrawText(text, x + 4, _y, rowFont, Text);
                }
                else
                {
                    DrawText(text, x + widths[c] - 4, _y, rowFont, Text, SKTextAlign.Right);
                }

                x += widths[c];
            }

            _canvas!.DrawLine(Margin, _y + 6, PageWidth - Margin, _y + 6, rule);
            _y += 18;
        }

        EnsureSpace(40);
        Header();
        for (var r = 0; r < rows.Count; r++)
        {
            if (_y + 18 > PageHeight - Margin - FooterSpace)
            {
                NewPage();
                Header();
            }

            Row(rows[r], boldLast && r == rows.Count - 1 ? bold : font);
        }

        _y += 14;
    }

    private void Chip(string text, SKColor color, float x, float y, float width)
    {
        using var fill = Fill(color.WithAlpha(28));
        using var border = Stroke(color.WithAlpha(110), 0.75f);
        var rect = SKRect.Create(x, y, width, 15);
        _canvas!.DrawRoundRect(rect, 3, 3, fill);
        _canvas.DrawRoundRect(rect, 3, 3, border);
        using var dot = Fill(color);
        _canvas.DrawRect(x + 6, y + 5, 5, 5, dot);
        using var font = Font(_bold, 8.5f);
        DrawText(text, x + 15, y + 11, font, Ink);
    }

    private void Heading(string text)
    {
        using var font = Font(_bold, 20);
        DrawText(text, Margin, _y + 6, font, Ink);
        _y += 36;
    }

    private void SubHeading(string text)
    {
        EnsureSpace(40);
        using var font = Font(_bold, 13);
        DrawText(text, Margin, _y, font, Ink);
        _y += 22;
    }

    // ---- page mechanics ----

    private SKCanvas NewPage(bool footer = true)
    {
        EndPage();
        _page++;
        _canvas = _document.BeginPage(PageWidth, PageHeight);
        _y = Margin + 12;
        _pageHasFooter = footer;
        return _canvas;
    }

    private void EndPage()
    {
        if (_canvas is null)
        {
            return;
        }

        if (_pageHasFooter)
        {
            using var font = Font(_regular, 8.5f);
            DrawText(_footer, Margin, PageHeight - Margin + 14, font, Muted);
            DrawText($"Page {_page}", PageWidth - Margin, PageHeight - Margin + 14, font, Muted, SKTextAlign.Right);
        }

        _document.EndPage();
        _canvas = null;
    }

    /// <summary>Starts a new page when the next block of this height does not fit (blocks taller than a page just flow).</summary>
    private void EnsureSpace(float height)
    {
        if (_y + height > PageHeight - Margin - FooterSpace && _y > Margin + 60)
        {
            NewPage();
        }
    }

    // ---- text ----

    private void DrawText(string text, float x, float y, SKFont font, SKColor color, SKTextAlign align = SKTextAlign.Left)
    {
        using var paint = Fill(color);
        _canvas!.DrawText(text, x, y, align, font, paint);
    }

    /// <summary>Lines of at most <paramref name="width"/>; paragraphs kept, long words split; "…" when cut at <paramref name="maxLines"/>.</summary>
    internal static IReadOnlyList<string> Wrap(string text, SKFont font, float width, int maxLines = int.MaxValue)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (font.MeasureText(candidate) <= width)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                }

                line = word;
                while (font.MeasureText(line) > width && line.Length > 1)
                {
                    var cut = line.Length - 1;
                    while (cut > 1 && font.MeasureText(line[..cut]) > width)
                    {
                        cut--;
                    }

                    lines.Add(line[..cut]);
                    line = line[cut..];
                }
            }

            lines.Add(line);
        }

        while (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count <= maxLines)
        {
            return lines;
        }

        var kept = lines.Take(maxLines).ToList();
        kept[^1] = Fit(kept[^1] + " …", font, width);
        return kept;
    }

    /// <summary>The text cut with "…" so it fits the width.</summary>
    private static string Fit(string text, SKFont font, float width)
    {
        if (font.MeasureText(text) <= width)
        {
            return text;
        }

        var cut = text.Length;
        while (cut > 0 && font.MeasureText(text[..cut] + "…") > width)
        {
            cut--;
        }

        return text[..cut].TrimEnd() + "…";
    }

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static SKFont Font(SKTypeface typeface, float size) => new(typeface, size) { Subpixel = true };

    private static SKPaint Fill(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint Stroke(SKColor color, float width) =>
        new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
}
