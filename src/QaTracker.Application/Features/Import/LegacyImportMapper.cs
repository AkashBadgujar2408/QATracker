using System.Net;
using System.Text.RegularExpressions;
using QaTracker.Domain.Enums;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Import;

public sealed record ImportUser(int Id, string FullName);

public sealed record ImportModuleRef(int Id, string Name);

/// <summary>A sheet and the module its issues go to (an existing module of the project, or a new one).</summary>
public sealed record ImportModule(int SheetIndex, string Sheet, string Name, int? ExistingModuleId, int Issues);

/// <summary>One issue as it will be created. <see cref="Warnings"/> empty = needs no manual correction.</summary>
/// <param name="Id">"sheet-row" (e.g. "2-14"), used to send corrections with the commit.</param>
public sealed record ImportRow(
    string Id,
    int SheetIndex,
    string Sheet,
    int Row,
    string Module,
    string Title,
    string? Steps,
    string ActualResult,
    string? ExpectedResult,
    string? Impact,
    Severity Severity,
    Priority Priority,
    int? AssigneeId,
    string? AssigneeName,
    string? ResolveBy,
    IssueStatus Status,
    string? HoldReason,
    RejectType? RejectType,
    IReadOnlyList<string> Comments,
    int Images,
    IReadOnlyList<string> Warnings);

/// <param name="ReadyRows">Rows without warnings.</param>
public sealed record ImportPreview(
    int SprintId,
    string SprintName,
    string ProjectKey,
    IReadOnlyList<ImportModule> Modules,
    IReadOnlyList<ImportRow> Rows,
    int ReadyRows,
    IReadOnlyList<string> Warnings);

/// <summary>Corrections from the preview grid, applied on commit.</summary>
public sealed record ImportCorrections(IReadOnlyList<ModuleCorrection>? Modules = null, IReadOnlyList<RowCorrection>? Rows = null);

public sealed record ModuleCorrection(int SheetIndex, string Name);

/// <param name="Unassign">Leave the issue unassigned (ignores <see cref="AssigneeId"/>).</param>
public sealed record RowCorrection(
    string Id,
    bool Skip = false,
    string? Title = null,
    Severity? Severity = null,
    Priority? Priority = null,
    int? AssigneeId = null,
    bool Unassign = false);

/// <summary>
/// Appendix C rules: sheet → module, "N. Title" → issue, labelled texts, "Severity: X / Priority: Y" (not assessed /
/// pending → Not assessed / Medium with a warning), Resolve By → developer by first name, Is Complete = Yes →
/// Verified, Pending Reason → On Hold, Valid/Invalid = Invalid → Rejected (Invalid), other texts → comments.
/// </summary>
public static partial class LegacyImportMapper
{
    public const int TitleMaxLength = 200;
    public const int ModuleNameMaxLength = 150;

    public static (IReadOnlyList<ImportModule> Modules, IReadOnlyList<ImportRow> Rows) Map(
        RawWorkbook workbook,
        IReadOnlyList<ImportModuleRef> existingModules,
        IReadOnlyList<ImportUser> developers)
    {
        var modules = new List<ImportModule>();
        var rows = new List<ImportRow>();
        for (var s = 0; s < workbook.Sheets.Count; s++)
        {
            var sheet = workbook.Sheets[s];
            if (sheet.Issues.Count == 0)
            {
                continue;
            }

            var name = ModuleName(sheet.Name);
            var existing = existingModules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            modules.Add(new ImportModule(s, sheet.Name, existing?.Name ?? name, existing?.Id, sheet.Issues.Count));
            rows.AddRange(sheet.Issues.Select(issue => Row(s, sheet.Name, existing?.Name ?? name, issue, developers)));
        }

        return (modules, rows);
    }

    public static string ModuleName(string sheet)
    {
        var name = Spaces().Replace(sheet, " ").Trim();
        return name.Length > ModuleNameMaxLength ? name[..ModuleNameMaxLength].TrimEnd() : name;
    }

    private static ImportRow Row(int sheetIndex, string sheet, string module, RawIssue raw, IReadOnlyList<ImportUser> developers)
    {
        var warnings = new List<string>();

        var title = raw.Title.Trim();
        if (title.Length > TitleMaxLength)
        {
            title = title[..(TitleMaxLength - 1)].TrimEnd() + "…";
            warnings.Add($"Title shortened to {TitleMaxLength} characters.");
        }

        var steps = Join(raw.Description, raw.Issue);
        var actual = Clean(raw.ActualResult);
        if (actual is null)
        {
            actual = steps ?? title;
            warnings.Add(steps is null ? "No Actual Result – the title was used." : "No Actual Result – the issue text was used.");
        }

        var severity = ParseSeverity(raw.Severity, warnings);
        var priority = ParsePriority(raw.Priority, warnings);
        var assignee = MatchDeveloper(raw.ResolveBy, developers, warnings);

        var (status, holdReason, rejectType) = StatusOf(raw, assignee);

        return new ImportRow(
            $"{sheetIndex + 1}-{raw.Row}",
            sheetIndex,
            sheet,
            raw.Row,
            module,
            title,
            steps,
            actual,
            Clean(raw.ExpectedResult),
            Clean(raw.Impact),
            severity,
            priority,
            assignee?.Id,
            assignee?.FullName,
            Clean(raw.ResolveBy),
            status,
            holdReason,
            rejectType,
            raw.OtherTexts.Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
            raw.Images.Count,
            warnings);
    }

    /// <summary>Applies a preview correction to a row (title, severity, priority, assignee); the warnings it settles go.</summary>
    public static ImportRow Apply(ImportRow row, RowCorrection correction, IReadOnlyList<ImportUser> developers)
    {
        var warnings = row.Warnings.ToList();
        var title = string.IsNullOrWhiteSpace(correction.Title) ? row.Title : correction.Title.Trim();
        if (!string.IsNullOrWhiteSpace(correction.Title))
        {
            warnings.RemoveAll(w => w.StartsWith("Title", StringComparison.Ordinal));
        }

        if (correction.Severity is not null)
        {
            warnings.RemoveAll(w => w.StartsWith("Severity", StringComparison.Ordinal));
        }

        if (correction.Priority is not null)
        {
            warnings.RemoveAll(w => w.StartsWith("Priority", StringComparison.Ordinal));
        }

        var assigneeId = row.AssigneeId;
        var assigneeName = row.AssigneeName;
        if (correction.Unassign)
        {
            (assigneeId, assigneeName) = (null, null);
            warnings.RemoveAll(w => w.StartsWith("Resolve By", StringComparison.Ordinal));
        }
        else if (correction.AssigneeId is { } id && developers.FirstOrDefault(d => d.Id == id) is { } developer)
        {
            (assigneeId, assigneeName) = (developer.Id, developer.FullName);
            warnings.RemoveAll(w => w.StartsWith("Resolve By", StringComparison.Ordinal));
        }

        var status = row.Status switch
        {
            IssueStatus.New when assigneeId is not null => IssueStatus.Assigned,
            IssueStatus.Assigned when assigneeId is null => IssueStatus.New,
            _ => row.Status,
        };

        return row with
        {
            Title = title.Length > TitleMaxLength ? title[..TitleMaxLength] : title,
            Severity = correction.Severity ?? row.Severity,
            Priority = correction.Priority ?? row.Priority,
            AssigneeId = assigneeId,
            AssigneeName = assigneeName,
            Status = status,
            Warnings = warnings,
        };
    }

    public static Severity ParseSeverity(string? text, List<string> warnings)
    {
        var value = Normalise(text);
        if (value.Length == 0 || NotAssessed().IsMatch(value))
        {
            warnings.Add("Severity not assessed in the workbook – set to Not assessed.");
            return Severity.NotAssessed;
        }

        if (Word(value, "critical", "blocker", "showstopper", "show stopper"))
        {
            return Severity.Critical;
        }

        if (Word(value, "high", "major"))
        {
            return Severity.High;
        }

        if (Word(value, "medium", "moderate", "normal"))
        {
            return Severity.Medium;
        }

        if (Word(value, "low", "minor", "trivial", "cosmetic"))
        {
            return Severity.Low;
        }

        warnings.Add($"Severity \"{text!.Trim()}\" not recognised – set to Not assessed.");
        return Severity.NotAssessed;
    }

    public static Priority ParsePriority(string? text, List<string> warnings)
    {
        var value = Normalise(text);
        if (value.Length == 0 || NotAssessed().IsMatch(value))
        {
            warnings.Add("Priority not set in the workbook – set to Medium.");
            return Priority.Medium;
        }

        if (Word(value, "high", "urgent", "immediate", "critical"))
        {
            return Priority.High;
        }

        if (Word(value, "medium", "normal", "moderate"))
        {
            return Priority.Medium;
        }

        if (Word(value, "low"))
        {
            return Priority.Low;
        }

        warnings.Add($"Priority \"{text!.Trim()}\" not recognised – set to Medium.");
        return Priority.Medium;
    }

    /// <summary>Resolve By → the active developer whose first name matches (case-insensitive); otherwise a warning.</summary>
    public static ImportUser? MatchDeveloper(string? resolveBy, IReadOnlyList<ImportUser> developers, List<string> warnings)
    {
        var text = Clean(resolveBy);
        if (text is null)
        {
            return null;
        }

        var names = NameSeparators().Split(text).Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        var first = names[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var matches = developers.Where(d => string.Equals(FirstName(d.FullName), first, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
        {
            if (names.Count > 1)
            {
                warnings.Add($"Resolve By names several people (\"{text}\") – assigned to {matches[0].FullName}.");
            }

            return matches[0];
        }

        warnings.Add(matches.Count == 0
            ? $"Resolve By \"{text}\" matches no active developer – left unassigned."
            : $"Resolve By \"{text}\" matches {matches.Count} developers – left unassigned.");
        return null;
    }

    private static (IssueStatus Status, string? HoldReason, RejectType? RejectType) StatusOf(RawIssue raw, ImportUser? assignee)
    {
        if (Normalise(raw.Validity) == "invalid")
        {
            return (IssueStatus.Rejected, null, RejectType.Invalid);
        }

        if (Normalise(raw.IsComplete) is "yes" or "y" or "done" or "completed" or "complete" or "true")
        {
            return (IssueStatus.Verified, null, null);
        }

        if (Clean(raw.PendingReason) is { } reason)
        {
            return (IssueStatus.OnHold, reason.Length > 1000 ? reason[..1000] : reason, null);
        }

        return (assignee is null ? IssueStatus.New : IssueStatus.Assigned, null, null);
    }

    /// <summary>Plain text → the rich-text HTML the app stores: a paragraph per line, text escaped.</summary>
    public static string? ToHtml(string? text) =>
        Clean(text) is { } clean
            ? string.Concat(clean.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>"))
            : null;

    private static string FirstName(string fullName) => fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    private static string? Join(params string?[] parts)
    {
        var text = string.Join('\n', parts.Select(Clean).OfType<string>());
        return text.Length == 0 ? null : text;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Replace("\r", "").Split('\n').Select(l => Spaces().Replace(l, " ").Trim());
        var clean = string.Join('\n', lines).Trim('\n', ' ');
        return clean.Length == 0 ? null : clean;
    }

    private static string Normalise(string? text) =>
        Spaces().Replace((text ?? "").Trim().TrimEnd('.', '…', '!').ToLowerInvariant(), " ");

    private static bool Word(string value, params string[] words) =>
        words.Any(w => Regex.IsMatch(value, $@"\b{Regex.Escape(w)}\b", RegexOptions.CultureInvariant));

    [GeneratedRegex(@"^(not\s*(yet\s*)?assessed|not\s*assigned|pending.*|tbd|to be decided|na|n/a|-+|\?+)$")]
    private static partial Regex NotAssessed();

    [GeneratedRegex(@"\s*(?:/|,|&|\band\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NameSeparators();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();
}
