namespace QaTracker.Application.Features.Import;

/// <summary>
/// What the legacy testing workbook contains, read as text (Appendix C) – the parser does not interpret severity,
/// people or statuses; <see cref="LegacyImportMapper"/> does.
/// </summary>
public sealed record RawWorkbook(IReadOnlyList<RawSheet> Sheets);

/// <param name="Warnings">Sheet-level notes (no header row found, images above the first issue …).</param>
public sealed record RawSheet(string Name, IReadOnlyList<RawIssue> Issues, IReadOnlyList<string> Warnings);

/// <summary>One "N. Title" block: its rows, the labelled texts and the status columns of the block.</summary>
/// <param name="Row">First row of the block (1-based, as Excel shows it).</param>
/// <param name="Description">Text after the title that has no label.</param>
/// <param name="OtherTexts">Text in columns the import does not know (e.g. "For your refer.") – becomes comments.</param>
public sealed record RawIssue(
    int Row,
    int LastRow,
    string Title,
    string? Description,
    string? Issue,
    string? ActualResult,
    string? ExpectedResult,
    string? Impact,
    string? Severity,
    string? Priority,
    string? ResolveBy,
    string? IsComplete,
    string? PendingReason,
    string? Validity,
    IReadOnlyList<string> OtherTexts,
    IReadOnlyList<RawImage> Images);

/// <summary>A floating picture anchored inside the block.</summary>
public sealed record RawImage(string FileName, byte[] Content);

/// <summary>Reads the legacy workbook (ClosedXML in Infrastructure).</summary>
public interface ILegacyWorkbookParser
{
    /// <summary>The workbook's sheets and issue blocks, or null when the bytes are not a readable .xlsx workbook.</summary>
    RawWorkbook? Parse(byte[] workbook);
}
