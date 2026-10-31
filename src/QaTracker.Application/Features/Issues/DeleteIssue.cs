using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Attachments;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Issues;

public sealed record DeleteIssueCommand(string Key);

/// <summary>
/// Hard delete – the only one in the system (plan 5): allowed while the sprint is in Testing. Activities, attachments,
/// watchers and outbound links cascade; inbound links and "duplicate of" references are removed first, attachment files after.
/// </summary>
public sealed class DeleteIssueHandler(
    IApplicationDbContext db,
    IFileStorage storage,
    ICurrentUser currentUser,
    ILogger<DeleteIssueHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(DeleteIssueCommand command, CancellationToken cancellationToken)
    {
        var key = IssueRules.NormalizeKey(command.Key);
        var issue = await db.Issues.Include(i => i.Sprint).SingleOrDefaultAsync(i => i.IssueKey == key, cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var check = SprintRules.CanChangeIssues(currentUser.Role, issue.Sprint.Status);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        // A carried-forward issue is part of the earlier sprint's frozen report (ADR-039).
        if (issue.CarriedFromSprintId is not null || await db.SprintSnapshots.AnyAsync(s => s.IssueId == issue.Id, cancellationToken))
        {
            return Error.Rule("issue_carried_forward", "This issue was carried forward from an earlier sprint and is part of its report, so it cannot be deleted.");
        }

        db.IssueLinks.RemoveRange(await db.IssueLinks.Where(l => l.ToIssueId == issue.Id).ToListAsync(cancellationToken));
        foreach (var duplicate in await db.Issues.Where(i => i.DuplicateOfIssueId == issue.Id).ToListAsync(cancellationToken))
        {
            duplicate.DuplicateOfIssueId = null;
        }

        var files = await db.Attachments.Where(a => a.IssueId == issue.Id)
            .Select(a => new { a.StoragePath, a.ThumbnailPath })
            .ToListAsync(cancellationToken);

        db.Issues.Remove(issue);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var path in files.SelectMany(f => new[] { f.StoragePath, f.ThumbnailPath }).OfType<string>())
        {
            await AttachmentFiles.TryDeleteAsync(storage, path, logger);
        }

        return true;
    }
}
