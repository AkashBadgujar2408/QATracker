using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Issues;

public sealed record DeleteIssueCommand(string Key);

/// <summary>
/// Hard delete – the only one in the system (plan 5): allowed while the sprint is in Testing. Activities, attachments,
/// watchers and outbound links cascade; inbound links and "duplicate of" references are removed here first.
/// </summary>
public sealed class DeleteIssueHandler(IApplicationDbContext db, ICurrentUser currentUser)
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

        db.IssueLinks.RemoveRange(await db.IssueLinks.Where(l => l.ToIssueId == issue.Id).ToListAsync(cancellationToken));
        foreach (var duplicate in await db.Issues.Where(i => i.DuplicateOfIssueId == issue.Id).ToListAsync(cancellationToken))
        {
            duplicate.DuplicateOfIssueId = null;
        }

        db.Issues.Remove(issue);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
