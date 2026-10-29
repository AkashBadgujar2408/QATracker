using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Analytics;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Reports;

public enum ReportFormat
{
    Xlsx,
    Pdf,
}

/// <summary>A downloadable report.</summary>
public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public sealed record SprintReportHeader(
    int SprintId,
    string SprintName,
    string PhaseName,
    SprintStatus Status,
    string ClientName,
    string ProjectKey,
    string ProjectName,
    DateTimeOffset StartedAt,
    DateTimeOffset? TestingCompletedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset GeneratedAt,
    string GeneratedBy);

/// <summary>One issue of the report. Rich-text fields are plain text with line breaks; thumbnails are the issue's own screenshots.</summary>
public sealed record SprintReportIssue(
    string Key,
    int Number,
    string Module,
    string? Screen,
    IssueType Type,
    string Title,
    string? Steps,
    string ActualResult,
    string? ExpectedResult,
    string? Impact,
    Severity Severity,
    Priority Priority,
    IssueStatus Status,
    string? Assignee,
    string Reporter,
    string? Environment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueDate,
    DateTimeOffset? FirstFixedAt,
    DateTimeOffset? ClosedAt,
    int ReopenCount,
    string? HoldReason,
    RejectType? RejectType,
    string? RejectReason,
    IReadOnlyList<byte[]> Thumbnails);

public sealed record ModuleSummary(string Module, int Total, IReadOnlyDictionary<StatusGroup, int> ByGroup, IReadOnlyDictionary<Severity, int> BySeverity);

public sealed record SprintReportSummary(
    int Total,
    IReadOnlyDictionary<StatusGroup, int> ByGroup,
    IReadOnlyDictionary<Severity, int> BySeverity,
    IReadOnlyList<ModuleSummary> ByModule);

/// <summary>Sprint report (plan 7 "Sprint report", S25): header, summary and every issue of the sprint.</summary>
public sealed record SprintReport(SprintReportHeader Header, IReadOnlyList<SprintReportIssue> Issues)
{
    public SprintReportSummary Summary { get; } = Summarise(Issues);

    public static StatusGroup GroupOf(IssueStatus status) =>
        Analytics.Analytics.Groups.First(g => g.Value.Contains(status)).Key;

    private static SprintReportSummary Summarise(IReadOnlyList<SprintReportIssue> issues)
    {
        static IReadOnlyDictionary<StatusGroup, int> Groups(IEnumerable<SprintReportIssue> items) =>
            Enum.GetValues<StatusGroup>().ToDictionary(g => g, g => items.Count(i => GroupOf(i.Status) == g));

        static IReadOnlyDictionary<Severity, int> Severities(IEnumerable<SprintReportIssue> items) =>
            Analytics.Analytics.SeverityOrder.ToDictionary(s => s, s => items.Count(i => i.Severity == s));

        var modules = issues
            .GroupBy(i => i.Module)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ModuleSummary(g.Key, g.Count(), Groups(g), Severities(g)))
            .ToList();

        return new SprintReportSummary(issues.Count, Groups(issues), Severities(issues), modules);
    }
}

/// <summary>Turns a sprint report into a file (ClosedXML / SkiaSharp in Infrastructure).</summary>
public interface IReportRenderer
{
    byte[] SprintExcel(SprintReport report);

    byte[] SprintPdf(SprintReport report);

    byte[] IssueListExcel(IssueExport export);
}

/// <summary>
/// Builds the sprint report. A closed sprint reports the status, severity and assignee frozen at closing
/// (SprintSnapshots) so later changes do not alter it.
/// </summary>
public sealed class SprintReportHandler(IApplicationDbContext db, IFileStorage storage, IReportRenderer renderer, ICurrentUser currentUser, IClock clock)
{
    /// <summary>Screenshots per issue in the report.</summary>
    public const int ThumbnailsPerIssue = 3;

    /// <summary>Thumbnails in one report at most (memory and file size).</summary>
    public const int MaxThumbnails = 600;

    public async Task<Result<ReportFile>> HandleAsync(int sprintId, ReportFormat format, CancellationToken cancellationToken)
    {
        var report = await BuildAsync(sprintId, cancellationToken);
        if (report is null)
        {
            return Error.NotFound("Sprint");
        }

        var name = ReportNames.File($"{report.Header.ProjectKey} {report.Header.SprintName} report {ReportText.Date(report.Header.GeneratedAt)}");
        return format == ReportFormat.Pdf
            ? new ReportFile(renderer.SprintPdf(report), "application/pdf", name + ".pdf")
            : new ReportFile(renderer.SprintExcel(report), ReportNames.XlsxContentType, name + ".xlsx");
    }

    public async Task<SprintReport?> BuildAsync(int sprintId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var header = await db.TestingSprints.AsNoTracking()
            .Where(s => s.Id == sprintId)
            .Select(s => new SprintReportHeader(
                s.Id, s.Name, s.Phase.Name, s.Status, s.Project.Client.Name, s.Project.ProjectKey, s.Project.Name,
                s.StartedAt, s.TestingCompletedAt, s.ClosedAt, now, ""))
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null)
        {
            return null;
        }

        var userName = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.FullName).SingleAsync(cancellationToken);
        header = header with { GeneratedBy = userName };

        var rows = await db.Issues.AsNoTracking()
            .Where(i => i.SprintId == sprintId)
            .OrderBy(i => i.Module.Name).ThenBy(i => i.Number)
            .Select(i => new
            {
                i.Id,
                i.IssueKey,
                i.Number,
                Module = i.Module.Name,
                i.ScreenName,
                i.Type,
                i.Title,
                i.Steps,
                i.ActualResult,
                i.ExpectedResult,
                i.Impact,
                i.Severity,
                i.Priority,
                i.Status,
                Assignee = i.Assignee == null ? null : i.Assignee.FullName,
                Reporter = i.Reporter.FullName,
                Environment = i.FoundOnEnvironment == null ? null : i.FoundOnEnvironment.Name,
                i.CreatedAt,
                i.DueDate,
                i.FirstFixedAt,
                i.ClosedAt,
                i.ReopenCount,
                i.HoldReason,
                i.RejectType,
                i.RejectReason,
            })
            .ToListAsync(cancellationToken);

        var snapshots = header.Status == SprintStatus.Closed
            ? await db.SprintSnapshots.AsNoTracking()
                .Where(s => s.SprintId == sprintId)
                .Select(s => new { s.IssueId, s.StatusAtClose, s.SeverityAtClose, Assignee = s.AssigneeAtClose == null ? null : s.AssigneeAtClose.FullName })
                .ToDictionaryAsync(s => s.IssueId, cancellationToken)
            : [];

        var thumbnails = await ThumbnailsAsync(rows.Select(r => r.Id).ToList(), cancellationToken);

        var issues = rows.Select(r =>
        {
            var snapshot = snapshots.GetValueOrDefault(r.Id);
            return new SprintReportIssue(
                r.IssueKey,
                r.Number,
                r.Module,
                r.ScreenName,
                r.Type,
                r.Title,
                ReportText.FromHtml(r.Steps),
                ReportText.FromHtml(r.ActualResult) ?? "",
                ReportText.FromHtml(r.ExpectedResult),
                ReportText.FromHtml(r.Impact),
                snapshot?.SeverityAtClose ?? r.Severity,
                r.Priority,
                snapshot?.StatusAtClose ?? r.Status,
                snapshot is null ? r.Assignee : snapshot.Assignee,
                r.Reporter,
                r.Environment,
                r.CreatedAt,
                r.DueDate,
                r.FirstFixedAt,
                r.ClosedAt,
                r.ReopenCount,
                r.HoldReason,
                r.RejectType,
                r.RejectReason,
                thumbnails.GetValueOrDefault(r.Id) ?? []);
        }).ToList();

        return new SprintReport(header, issues);
    }

    /// <summary>The first screenshots of each issue (uploaded to the issue, not those of comments) as stored thumbnails.</summary>
    private async Task<Dictionary<int, IReadOnlyList<byte[]>>> ThumbnailsAsync(IReadOnlyList<int> issueIds, CancellationToken cancellationToken)
    {
        var attachments = await db.Attachments.AsNoTracking()
            .Where(a => issueIds.Contains(a.IssueId) && a.ThumbnailPath != null && (a.Activity == null || a.Activity.Kind == ActivityKind.Attachment))
            .OrderBy(a => a.IssueId).ThenBy(a => a.UploadedAt).ThenBy(a => a.Id)
            .Select(a => new { a.IssueId, Path = a.ThumbnailPath! })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<int, IReadOnlyList<byte[]>>();
        var total = 0;
        foreach (var issue in attachments.GroupBy(a => a.IssueId))
        {
            var images = new List<byte[]>();
            foreach (var attachment in issue.Take(ThumbnailsPerIssue))
            {
                if (total >= MaxThumbnails)
                {
                    break;
                }

                await using var stream = storage.OpenRead(attachment.Path);
                if (stream is null)
                {
                    continue;
                }

                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, cancellationToken);
                images.Add(copy.ToArray());
                total++;
            }

            result[issue.Key] = images;
        }

        return result;
    }
}

/// <summary>Text helpers shared by the report writers.</summary>
public static partial class ReportText
{
    /// <summary>27/Oct/2026 in IST (plan 10: dates dd/MMM/yyyy).</summary>
    public static string Date(DateTimeOffset value) =>
        value.ToOffset(WorkingCalendar.Ist).ToString("dd'/'MMM'/'yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateTimeOffset? value) => value is { } v ? Date(v) : "";

    /// <summary>27/Oct/2026 10:15 in IST.</summary>
    public static string DateTime(DateTimeOffset value) =>
        value.ToOffset(WorkingCalendar.Ist).ToString("dd'/'MMM'/'yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Plain text of sanitised rich text: paragraphs, line breaks and list items become lines.</summary>
    public static string? FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = LineBreaks().Replace(html, "\n");
        text = ListItems().Replace(text, "\n• ");
        text = WebUtility.HtmlDecode(Tags().Replace(text, ""));
        var lines = text.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        var result = string.Join('\n', lines);
        return result.Length == 0 ? null : result;
    }

    public static string Status(IssueStatus status) => status switch
    {
        IssueStatus.InProgress => "In Progress",
        IssueStatus.NeedInfo => "Need Info",
        IssueStatus.OnHold => "On Hold",
        _ => status.ToString(),
    };

    public static string Severity(Severity severity) => severity == Domain.Enums.Severity.NotAssessed ? "Not assessed" : severity.ToString();

    public static string Reject(RejectType? type) => type switch
    {
        RejectType.AsDesigned => "As designed",
        RejectType.WontFix => "Won't fix",
        null => "",
        _ => type.Value.ToString(),
    };

    public static string SprintStatus(SprintStatus status) => status == Domain.Enums.SprintStatus.TestingCompleted ? "Testing completed" : status.ToString();

    [GeneratedRegex(@"<\s*(br|/p|/div|/h[1-6]|/li)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<\s*li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItems();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex Spaces();
}

public static partial class ReportNames
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>A file name without characters Windows or browsers reject ("PGERP Sprint 2 – UAT report 29-Oct-2026").</summary>
    public static string File(string name)
    {
        var clean = Invalid().Replace(name, "-");
        clean = Spaces().Replace(clean, " ").Trim().TrimEnd('.');
        return clean.Length > 120 ? clean[..120].TrimEnd() : clean;
    }

    [GeneratedRegex(@"[\\/:*?""<>|\x00-\x1f]")]
    private static partial Regex Invalid();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
