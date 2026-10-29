using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Attachments;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Import;

/// <param name="Workbook">The uploaded .xlsx.</param>
public sealed record ImportPreviewCommand(int SprintId, IncomingFile Workbook);

public sealed record ImportCommitCommand(int SprintId, IncomingFile Workbook, ImportCorrections Corrections);

/// <param name="FirstKey">Key of the first created issue (null when nothing was imported).</param>
/// <param name="Warnings">Notes from creating the issues (e.g. an image that could not be stored).</param>
public sealed record ImportResult(int Created, int Skipped, int ModulesCreated, int Attachments, string? FirstKey, string? LastKey, IReadOnlyList<string> Warnings);

/// <summary>Shared steps of preview and commit: the target sprint, the rules, the parse and the mapping.</summary>
public sealed class LegacyImportReader(IApplicationDbContext db, ILegacyWorkbookParser parser, ICurrentUser currentUser)
{
    /// <summary>The legacy workbook has ~90 screenshots; 30 MB leaves room.</summary>
    public const long MaxWorkbookBytes = 30 * 1024 * 1024;

    public sealed record Target(int SprintId, string SprintName, int ProjectId, string ProjectKey, int? DefaultEnvironmentId);

    public sealed record Parsed(Target Sprint, RawWorkbook Workbook, IReadOnlyList<ImportModule> Modules, IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportUser> Developers);

    public async Task<Result<Parsed>> ReadAsync(int sprintId, IncomingFile file, CancellationToken cancellationToken)
    {
        var sprint = await db.TestingSprints.AsNoTracking()
            .Where(s => s.Id == sprintId)
            .Select(s => new { s.Id, s.Name, s.ProjectId, s.Project.ProjectKey, s.Status, s.DefaultEnvironmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (sprint is null)
        {
            return Error.NotFound("Sprint");
        }

        var check = SprintRules.CanChangeIssues(currentUser.Role, sprint.Status);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        if (file.Content.Length > MaxWorkbookBytes)
        {
            return Error.Validation("file", "The workbook is larger than 30 MB.");
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || parser.Parse(file.Content) is not { } workbook)
        {
            return Error.Validation("file", "Choose the testing workbook as an Excel .xlsx file.");
        }

        var modules = await db.Modules.AsNoTracking()
            .Where(m => m.ProjectId == sprint.ProjectId && m.IsActive)
            .Select(m => new ImportModuleRef(m.Id, m.Name))
            .ToListAsync(cancellationToken);
        var developers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role != UserRole.Tester)
            .OrderBy(u => u.FullName)
            .Select(u => new ImportUser(u.Id, u.FullName))
            .ToListAsync(cancellationToken);

        var (mappedModules, rows) = LegacyImportMapper.Map(workbook, modules, developers);
        return new Parsed(
            new Target(sprint.Id, sprint.Name, sprint.ProjectId, sprint.ProjectKey, sprint.DefaultEnvironmentId),
            workbook,
            mappedModules,
            rows,
            developers);
    }
}

/// <summary>Parses the uploaded legacy workbook for the preview grid; nothing is saved.</summary>
public sealed class ImportPreviewHandler(LegacyImportReader reader)
{
    public async Task<Result<ImportPreview>> HandleAsync(ImportPreviewCommand command, CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(command.SprintId, command.Workbook, cancellationToken);
        if (!read.IsSuccess)
        {
            return read.Error;
        }

        var p = read.Value;
        var warnings = p.Workbook.Sheets.SelectMany(s => s.Warnings.Select(w => $"{s.Name}: {w}")).ToList();
        if (p.Rows.Count == 0)
        {
            warnings.Add("No issues found – each issue starts with \"1. Title\" in the issue column.");
        }

        return new ImportPreview(p.Sprint.SprintId, p.Sprint.SprintName, p.Sprint.ProjectKey, p.Modules, p.Rows, p.Rows.Count(r => r.Warnings.Count == 0), warnings);
    }
}

/// <summary>
/// Creates the previewed issues in one transaction: missing modules, issues numbered in order (PGERP-143 …) with their
/// imported status, a Created activity (plus Assigned / StatusChanged for the imported state), other texts as
/// comments, and the anchored screenshots as attachments. The upload is parsed again, so the preview needs no storage.
/// </summary>
public sealed class ImportCommitHandler(
    LegacyImportReader reader,
    IApplicationDbContext db,
    IIssueNumbering numbering,
    IRichTextSanitizer sanitizer,
    AttachmentWriter attachments,
    ICurrentUser currentUser,
    IClock clock)
{
    public const string ImportNote = "Imported from the Excel testing workbook.";

    public async Task<Result<ImportResult>> HandleAsync(ImportCommitCommand command, CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(command.SprintId, command.Workbook, cancellationToken);
        if (!read.IsSuccess)
        {
            return read.Error;
        }

        var p = read.Value;
        var corrections = (command.Corrections.Rows ?? []).ToDictionary(c => c.Id);
        var unknown = corrections.Keys.Where(id => p.Rows.All(r => r.Id != id)).ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation("corrections", $"Rows {string.Join(", ", unknown)} are not in this workbook – preview it again.");
        }

        var moduleNames = p.Modules.ToDictionary(m => m.SheetIndex, m => m.Name);
        foreach (var rename in command.Corrections.Modules ?? [])
        {
            var name = LegacyImportMapper.ModuleName(rename.Name);
            if (!moduleNames.ContainsKey(rename.SheetIndex) || name.Length == 0)
            {
                return Error.Validation("corrections", "A module name is empty or belongs to no sheet – preview it again.");
            }

            moduleNames[rename.SheetIndex] = name;
        }

        var rows = p.Rows
            .Where(r => !(corrections.GetValueOrDefault(r.Id)?.Skip ?? false))
            .Select(r => corrections.TryGetValue(r.Id, out var c) ? LegacyImportMapper.Apply(r, c, p.Developers) : r)
            .ToList();
        var skipped = p.Rows.Count - rows.Count;
        if (rows.Count == 0)
        {
            return Error.Rule("import_empty", "There is nothing to import – every row is skipped or the workbook has no issues.");
        }

        var images = p.Workbook.Sheets
            .SelectMany((sheet, s) => sheet.Issues.Select(i => (Id: $"{s + 1}-{i.Row}", i.Images)))
            .ToDictionary(x => x.Id, x => x.Images);

        var now = clock.UtcNow;
        var warnings = new List<string>();
        var modulesCreated = 0;
        var attachmentCount = 0;
        var keys = new List<string>();

        await numbering.SaveWithNextNumberAsync(p.Sprint.ProjectId, async first =>
        {
            var modules = await ModulesAsync(p.Sprint.ProjectId, moduleNames.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), now, cancellationToken);
            modulesCreated = modules.Created;

            var created = new List<(Issue Issue, ImportRow Row)>();
            var number = first;
            foreach (var row in rows)
            {
                var issue = NewIssue(p.Sprint, modules.Ids[moduleNames[row.SheetIndex]], row, number++, now);
                db.Issues.Add(issue);
                created.Add((issue, row));
            }

            await db.SaveChangesAsync(cancellationToken);

            foreach (var (issue, row) in created)
            {
                var files = new List<(IncomingFile File, FileKind Kind)>();
                foreach (var image in images.GetValueOrDefault(row.Id) ?? [])
                {
                    var (kind, error) = FileInspector.Inspect(image.FileName, image.Content);
                    if (kind is { IsImage: true })
                    {
                        files.Add((new IncomingFile(image.FileName, image.Content), kind));
                    }
                    else
                    {
                        warnings.Add($"{issue.IssueKey}: screenshot {image.FileName} was not stored ({error ?? "not an image"}).");
                    }
                }

                if (files.Count > 0)
                {
                    var issueId = issue.Id;
                    await attachments.SaveAsync(
                        issueId,
                        files,
                        name => new IssueActivity { IssueId = issueId, UserId = currentUser.UserId, Kind = ActivityKind.Attachment, Body = name, CreatedAt = now },
                        now,
                        cancellationToken);
                    attachmentCount += files.Count;
                }

                keys.Add(issue.IssueKey);
            }

            return keys.Count;
        }, cancellationToken);

        return new ImportResult(keys.Count, skipped, modulesCreated, attachmentCount, keys.FirstOrDefault(), keys.LastOrDefault(), warnings);
    }

    private async Task<(Dictionary<string, int> Ids, int Created)> ModulesAsync(int projectId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Modules
            .Where(m => m.ProjectId == projectId)
            .Select(m => new { m.Id, m.Name, m.IsActive })
            .ToListAsync(cancellationToken);

        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var added = new List<Module>();
        foreach (var name in names)
        {
            if (existing.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) is { } module)
            {
                if (!module.IsActive)
                {
                    await db.Modules.Where(m => m.Id == module.Id).ExecuteUpdateAsync(
                        u => u.SetProperty(m => m.IsActive, true).SetProperty(m => m.UpdatedAt, now).SetProperty(m => m.UpdatedById, currentUser.UserId),
                        cancellationToken);
                }

                ids[name] = module.Id;
                continue;
            }

            var created = new Module { ProjectId = projectId, Name = name, IsActive = true, CreatedAt = now, CreatedById = currentUser.UserId };
            db.Modules.Add(created);
            added.Add(created);
        }

        if (added.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            foreach (var module in added)
            {
                ids[module.Name] = module.Id;
            }
        }

        return (ids, added.Count);
    }

    private Issue NewIssue(LegacyImportReader.Target sprint, int moduleId, ImportRow row, int number, DateTimeOffset now)
    {
        var user = currentUser.UserId;
        var issue = new Issue
        {
            ProjectId = sprint.ProjectId,
            SprintId = sprint.SprintId,
            Number = number,
            IssueKey = $"{sprint.ProjectKey}-{number}",
            ModuleId = moduleId,
            FoundOnEnvironmentId = sprint.DefaultEnvironmentId,
            Type = IssueType.Bug,
            Title = row.Title,
            Steps = sanitizer.Sanitize(LegacyImportMapper.ToHtml(row.Steps)),
            ActualResult = sanitizer.Sanitize(LegacyImportMapper.ToHtml(row.ActualResult)) ?? $"<p>{System.Net.WebUtility.HtmlEncode(row.Title)}</p>",
            ExpectedResult = sanitizer.Sanitize(LegacyImportMapper.ToHtml(row.ExpectedResult)),
            Impact = sanitizer.Sanitize(LegacyImportMapper.ToHtml(row.Impact)),
            Severity = row.Severity,
            Priority = row.Priority,
            Status = row.Status,
            HoldReason = row.HoldReason,
            RejectType = row.RejectType,
            RejectReason = row.RejectType is null ? null : "Marked Invalid in the imported workbook.",
            ReporterId = user,
            AssigneeId = row.AssigneeId,
            ReopenCount = 0,
            CreatedAt = now,
            VerifiedAt = row.Status == IssueStatus.Verified ? now : null,
        };

        issue.IssueActivities.Add(new IssueActivity { UserId = user, Kind = ActivityKind.Created, Body = ImportNote, CreatedAt = now });
        foreach (var watcher in new[] { user, row.AssigneeId }.OfType<int>().Distinct())
        {
            issue.IssueWatchers.Add(new IssueWatcher { UserId = watcher, AddedAt = now });
        }

        if (row.AssigneeId is { } assigneeId)
        {
            issue.IssueActivities.Add(new IssueActivity
            {
                UserId = user,
                Kind = ActivityKind.Assigned,
                NewValue = assigneeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CreatedAt = now,
            });
        }

        // The imported state is a status change from New, so the timeline and the analytics history see it.
        if (row.Status is not (IssueStatus.New or IssueStatus.Assigned))
        {
            issue.IssueActivities.Add(new IssueActivity
            {
                UserId = user,
                Kind = ActivityKind.StatusChanged,
                OldValue = (row.AssigneeId is null ? IssueStatus.New : IssueStatus.Assigned).ToString(),
                NewValue = row.Status.ToString(),
                Body = row.HoldReason ?? (row.RejectType is null ? ImportNote : "Marked Invalid in the imported workbook."),
                CreatedAt = now,
            });
        }

        foreach (var comment in row.Comments)
        {
            issue.IssueActivities.Add(new IssueActivity { UserId = user, Kind = ActivityKind.Comment, Body = comment, CreatedAt = now });
        }

        return issue;
    }
}
