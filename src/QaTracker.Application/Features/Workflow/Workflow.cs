using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Issues;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Workflow;

public sealed record AssignIssueCommand(string Key, string RowVersion, int UserId, string? Note);

public sealed record TransitionIssueCommand(
    string Key,
    string RowVersion,
    IssueAction? Action,
    string? Reason,
    RejectType? RejectType,
    string? DuplicateOfKey);

public sealed record MarkFixedCommand(string Key, string RowVersion, string? Note, IReadOnlyList<int>? EnvironmentIds, string? Version);

public sealed record RetestCommand(int DeploymentId, bool Passed, string? Comment);

public sealed record BulkAssignCommand(IReadOnlyList<string>? Keys, int UserId, string? Note);

public sealed record BulkAssignFailure(string Key, string Code, string Reason);

public sealed record BulkAssignResult(IReadOnlyList<string> Assigned, IReadOnlyList<BulkAssignFailure> Failed);

internal static class WorkflowLimits
{
    public const int ReasonMaxLength = 2000;
    public const int VersionMaxLength = 100;
    public const int BulkMax = 200;
}

public sealed class AssignIssueValidator : AbstractValidator<AssignIssueCommand>
{
    public AssignIssueValidator()
    {
        RuleFor(x => x.RowVersion).ValidRowVersion();
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("Choose who should work on the issue.");
        RuleFor(x => x.Note).MaximumLength(WorkflowLimits.ReasonMaxLength);
    }
}

public sealed class TransitionIssueValidator : AbstractValidator<TransitionIssueCommand>
{
    /// <summary>Assign, Pick and MarkFixed have their own endpoints (they need more than a reason).</summary>
    public static readonly IReadOnlySet<IssueAction> OwnEndpoint = new HashSet<IssueAction> { IssueAction.Assign, IssueAction.Pick, IssueAction.MarkFixed };

    public TransitionIssueValidator()
    {
        RuleFor(x => x.RowVersion).ValidRowVersion();
        RuleFor(x => x.Action).NotNull().IsInEnum()
            .Must(action => action is null || !OwnEndpoint.Contains(action.Value))
            .WithMessage("Use the assign, pick or fixed endpoint for this action.");
        RuleFor(x => x.Reason).MaximumLength(WorkflowLimits.ReasonMaxLength);
        RuleFor(x => x.Reason).NotEmpty()
            .When(x => x.Action is { } action && Enum.IsDefined(action) && IssueWorkflow.InputsOf(action).HasFlag(ActionInput.Reason))
            .WithMessage(x => x.Action switch
            {
                IssueAction.NeedInfo => "Write the question for the reporter.",
                IssueAction.Contest => "Explain why this is still a bug.",
                _ => "A reason is required.",
            });
        RuleFor(x => x.RejectType).NotNull().IsInEnum().When(x => x.Action == IssueAction.Reject).WithMessage("Choose why the issue is rejected.");
        RuleFor(x => x.DuplicateOfKey).NotEmpty()
            .When(x => x.Action == IssueAction.Reject && x.RejectType == RejectType.Duplicate)
            .WithMessage("Enter the key of the issue this one duplicates.");
    }
}

public sealed class MarkFixedValidator : AbstractValidator<MarkFixedCommand>
{
    public MarkFixedValidator()
    {
        RuleFor(x => x.RowVersion).ValidRowVersion();
        RuleFor(x => x.Note).NotEmpty().WithMessage("Describe the fix.").MaximumLength(WorkflowLimits.ReasonMaxLength);
        RuleFor(x => x.EnvironmentIds).NotEmpty().WithMessage("Choose at least one environment the fix is deployed on.");
        RuleFor(x => x.Version).MaximumLength(WorkflowLimits.VersionMaxLength);
    }
}

public sealed class RetestValidator : AbstractValidator<RetestCommand>
{
    public RetestValidator()
    {
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.ReasonMaxLength);
        RuleFor(x => x.Comment).NotEmpty().When(x => !x.Passed).WithMessage("Describe what still fails.");
    }
}

public sealed class BulkAssignValidator : AbstractValidator<BulkAssignCommand>
{
    public BulkAssignValidator()
    {
        RuleFor(x => x.Keys).NotEmpty().WithMessage("Select at least one issue.")
            .Must(keys => keys is null || keys.Count <= WorkflowLimits.BulkMax).WithMessage($"Assign at most {WorkflowLimits.BulkMax} issues at once.");
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("Choose who should work on the issues.");
        RuleFor(x => x.Note).MaximumLength(WorkflowLimits.ReasonMaxLength);
    }
}

/// <summary>Shared steps of the workflow handlers: load, check with IssueWorkflow, write activities, reload the DTO.</summary>
public sealed class WorkflowSupport(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    public DateTimeOffset Now { get; } = clock.UtcNow;

    public int UserId => currentUser.UserId;

    public Task<Issue?> LoadAsync(string key, CancellationToken cancellationToken)
    {
        var normalized = IssueRules.NormalizeKey(key);
        return db.Issues.Include(i => i.Sprint).Include(i => i.Project).SingleOrDefaultAsync(i => i.IssueKey == normalized, cancellationToken);
    }

    public async Task<RuleCheck> CheckAsync(Issue issue, IssueAction action, CancellationToken cancellationToken)
    {
        var hasPassedLive = action == IssueAction.Close && issue.Project.RequireLiveVerification
            && await db.Deployments.AnyAsync(d => d.IssueId == issue.Id && d.Environment.Type == EnvironmentType.LIVE && d.RetestResult == RetestResult.Passed, cancellationToken);
        var context = new WorkflowContext(
            issue.Status, issue.Sprint.Status, issue.AssigneeId, currentUser.Role, currentUser.UserId, issue.Project.RequireLiveVerification, hasPassedLive);
        return IssueWorkflow.Check(context, action);
    }

    /// <summary>The assignee must be an active developer or admin.</summary>
    public async Task<Error?> CheckAssigneeAsync(int userId, CancellationToken cancellationToken) =>
        await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Role != UserRole.Tester, cancellationToken)
            ? null
            : Error.Validation("userId", "Assign the issue to an active developer.");

    /// <summary>"X assigned to Y" (OldValue/NewValue = user ids); a pick is an assignment to oneself from nobody.</summary>
    public void Assigned(Issue issue, int? from, int to, string? note) => db.IssueActivities.Add(new IssueActivity
    {
        IssueId = issue.Id,
        UserId = currentUser.UserId,
        Kind = ActivityKind.Assigned,
        OldValue = from?.ToString(CultureInfo.InvariantCulture),
        NewValue = to.ToString(CultureInfo.InvariantCulture),
        Body = Clean(note),
        CreatedAt = Now,
    });

    public void StatusChanged(Issue issue, IssueStatus from, string? reason)
    {
        if (from == issue.Status)
        {
            return;
        }

        db.IssueActivities.Add(new IssueActivity
        {
            IssueId = issue.Id,
            UserId = currentUser.UserId,
            Kind = ActivityKind.StatusChanged,
            OldValue = from.ToString(),
            NewValue = issue.Status.ToString(),
            Body = Clean(reason),
            CreatedAt = Now,
        });
    }

    /// <summary>A comment that mentions the user (Need info: the question goes to the reporter).</summary>
    public void CommentMentioning(Issue issue, string text, int mentionedUserId)
    {
        var comment = new IssueActivity
        {
            IssueId = issue.Id,
            UserId = currentUser.UserId,
            Kind = ActivityKind.Comment,
            Body = Clean(text),
            CreatedAt = Now,
        };
        db.IssueActivities.Add(comment);
        if (mentionedUserId != currentUser.UserId)
        {
            db.Mentions.Add(new Mention { Activity = comment, UserId = mentionedUserId });
        }
    }

    public void Retested(Deployment deployment, bool passed, string? comment)
    {
        deployment.RetestResult = passed ? RetestResult.Passed : RetestResult.Failed;
        deployment.RetestedById = currentUser.UserId;
        deployment.RetestedAt = Now;
        deployment.RetestComment = Clean(comment);
        db.IssueActivities.Add(new IssueActivity
        {
            IssueId = deployment.IssueId,
            UserId = currentUser.UserId,
            Kind = passed ? ActivityKind.RetestPassed : ActivityKind.RetestFailed,
            Body = Clean(comment),
            EnvironmentId = deployment.EnvironmentId,
            Deployment = deployment,
            CreatedAt = Now,
        });
    }

    /// <summary>Pending deployments of the issue (retests still to do).</summary>
    public Task<List<Deployment>> PendingDeploymentsAsync(int issueId, CancellationToken cancellationToken) =>
        db.Deployments.Where(d => d.IssueId == issueId && d.RetestResult == RetestResult.Pending).ToListAsync(cancellationToken);

    public async Task<Result<IssueDto>> SaveAndReloadAsync(Issue issue, string? rowVersion, CancellationToken cancellationToken)
    {
        if (rowVersion is not null)
        {
            db.ExpectRowVersion(issue, rowVersion);
        }

        issue.UpdatedAt = Now;
        if (await db.TrySaveAsync("This issue", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await ReloadAsync(issue.Id, cancellationToken);
    }

    public async Task<IssueDto> ReloadAsync(int issueId, CancellationToken cancellationToken)
    {
        var dto = await db.Issues.AsNoTracking().Where(i => i.Id == issueId).Select(IssueDto.Projection).SingleAsync(cancellationToken);
        return await IssueAccess.WithAccessAsync(db, currentUser, dto, cancellationToken);
    }

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>Assign or reassign (plan 7.3). New → Assigned; other statuses keep theirs. Tester may pre-assign during Testing.</summary>
public sealed class AssignIssueHandler(IValidator<AssignIssueCommand> validator, IApplicationDbContext db, WorkflowSupport workflow)
{
    public async Task<Result<IssueDto>> HandleAsync(AssignIssueCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var issue = await workflow.LoadAsync(command.Key, cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var check = await workflow.CheckAsync(issue, IssueAction.Assign, cancellationToken);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        if (await workflow.CheckAssigneeAsync(command.UserId, cancellationToken) is { } badAssignee)
        {
            return badAssignee;
        }

        if (issue.AssigneeId == command.UserId)
        {
            var name = await db.Users.Where(u => u.Id == command.UserId).Select(u => u.FullName).SingleAsync(cancellationToken);
            return Error.Rule("already_assignee", $"{issue.IssueKey} is already assigned to {name}.");
        }

        var from = issue.Status;
        workflow.Assigned(issue, issue.AssigneeId, command.UserId, command.Note);
        issue.AssigneeId = command.UserId;
        issue.Status = IssueWorkflow.TargetOf(from, IssueAction.Assign);
        workflow.StatusChanged(issue, from, null);

        return await workflow.SaveAndReloadAsync(issue, command.RowVersion, cancellationToken);
    }
}

/// <summary>
/// Pick (plan 7.3): atomic self-assign of an unassigned issue and start it –
/// <c>UPDATE … SET AssigneeId = @me WHERE Id = @id AND AssigneeId IS NULL</c>; zero rows → "Already picked by X".
/// </summary>
public sealed class PickIssueHandler(IApplicationDbContext db, WorkflowSupport workflow)
{
    public async Task<Result<IssueDto>> HandleAsync(string key, CancellationToken cancellationToken)
    {
        var issue = await workflow.LoadAsync(key, cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var check = await workflow.CheckAsync(issue, IssueAction.Pick, cancellationToken);
        if (!check.Allowed)
        {
            return check.Code == "already_assigned" ? await AlreadyPickedAsync(issue.Id, cancellationToken) : check.ToError();
        }

        var from = issue.Status;
        var target = IssueWorkflow.TargetOf(from, IssueAction.Pick);
        var me = workflow.UserId;
        var now = workflow.Now;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var picked = await db.Issues
            .Where(i => i.Id == issue.Id && i.AssigneeId == null && i.Status == from)
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.AssigneeId, me)
                .SetProperty(i => i.Status, target)
                .SetProperty(i => i.UpdatedAt, now), cancellationToken);
        if (picked == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await AlreadyPickedAsync(issue.Id, cancellationToken);
        }

        // The row was updated in SQL; the loaded entity only supplies the id for the activities.
        db.Entry(issue).State = EntityState.Detached;
        workflow.Assigned(issue, null, me, null);
        issue.Status = target;
        workflow.StatusChanged(issue, from, null);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await workflow.ReloadAsync(issue.Id, cancellationToken);
    }

    private async Task<Error> AlreadyPickedAsync(int issueId, CancellationToken cancellationToken)
    {
        var holder = await db.Issues.AsNoTracking().Where(i => i.Id == issueId).Select(i => i.Assignee != null ? i.Assignee.FullName : null).SingleAsync(cancellationToken);
        return holder is null
            ? Error.Conflict("concurrency", "This issue changed while you were looking at it. Reload and try again.")
            : Error.Conflict("already_picked", $"Already picked by {holder}.");
    }
}

/// <summary>Start, need info, resume, hold, unhold, dispute, reject, contest, verify, reopen, close (plan 7.2).</summary>
public sealed class TransitionIssueHandler(IValidator<TransitionIssueCommand> validator, IApplicationDbContext db, WorkflowSupport workflow)
{
    public async Task<Result<IssueDto>> HandleAsync(TransitionIssueCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var action = command.Action!.Value;
        var issue = await workflow.LoadAsync(command.Key, cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var check = await workflow.CheckAsync(issue, action, cancellationToken);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        var from = issue.Status;
        issue.Status = IssueWorkflow.TargetOf(from, action);
        var reason = WorkflowSupport.Clean(command.Reason);
        var statusNote = reason;

        switch (action)
        {
            case IssueAction.NeedInfo:
                workflow.CommentMentioning(issue, reason!, issue.ReporterId);
                statusNote = null; // the question is the comment
                break;
            case IssueAction.Hold:
                issue.HoldReason = reason;
                break;
            case IssueAction.Unhold:
                issue.HoldReason = null;
                break;
            case IssueAction.Reject:
                if (await RejectAsync(issue, command, reason, cancellationToken) is { } rejectError)
                {
                    return rejectError;
                }

                break;
            case IssueAction.Verify:
                foreach (var deployment in await workflow.PendingDeploymentsAsync(issue.Id, cancellationToken))
                {
                    workflow.Retested(deployment, passed: true, reason);
                }

                issue.VerifiedAt = workflow.Now;
                break;
            case IssueAction.Reopen:
                if (from == IssueStatus.Fixed)
                {
                    foreach (var deployment in await workflow.PendingDeploymentsAsync(issue.Id, cancellationToken))
                    {
                        workflow.Retested(deployment, passed: false, reason);
                    }
                }

                issue.ReopenCount++;
                issue.VerifiedAt = null;
                issue.ClosedAt = null;
                break;
            case IssueAction.Close:
                issue.ClosedAt = workflow.Now;
                break;
        }

        workflow.StatusChanged(issue, from, statusNote);
        return await workflow.SaveAndReloadAsync(issue, command.RowVersion, cancellationToken);
    }

    private async Task<Error?> RejectAsync(Issue issue, TransitionIssueCommand command, string? reason, CancellationToken cancellationToken)
    {
        issue.RejectType = command.RejectType;
        issue.RejectReason = reason;
        if (command.RejectType != RejectType.Duplicate)
        {
            return null;
        }

        var originalKey = IssueRules.NormalizeKey(command.DuplicateOfKey!);
        var original = await db.Issues.Where(i => i.IssueKey == originalKey && i.ProjectId == issue.ProjectId).Select(i => (int?)i.Id).SingleOrDefaultAsync(cancellationToken);
        if (original is null || original == issue.Id)
        {
            return Error.Validation("duplicateOfKey", "Enter the key of another issue of this project.");
        }

        issue.DuplicateOfIssueId = original;
        if (!await db.IssueLinks.AnyAsync(l => l.FromIssueId == issue.Id && l.ToIssueId == original, cancellationToken))
        {
            db.IssueLinks.Add(new IssueLink { FromIssueId = issue.Id, ToIssueId = original.Value, Type = IssueLinkType.Duplicate, CreatedAt = workflow.Now });
        }

        return null;
    }
}

/// <summary>
/// Fixed (plan 7.4): one Deployment (retest pending) and one Deployed activity per environment. From Verified or Fixed
/// it records a further deployment, e.g. LIVE after UAT passed.
/// </summary>
public sealed class MarkFixedHandler(IValidator<MarkFixedCommand> validator, IApplicationDbContext db, WorkflowSupport workflow)
{
    public async Task<Result<IssueDto>> HandleAsync(MarkFixedCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var issue = await workflow.LoadAsync(command.Key, cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var check = await workflow.CheckAsync(issue, IssueAction.MarkFixed, cancellationToken);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        var environmentIds = command.EnvironmentIds!.Distinct().ToList();
        var environments = await db.ProjectEnvironments
            .Where(e => environmentIds.Contains(e.Id) && e.ProjectId == issue.ProjectId && e.IsActive)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        if (environments.Count != environmentIds.Count)
        {
            return Error.Validation("environmentIds", "Choose active environments of this project.");
        }

        var note = WorkflowSupport.Clean(command.Note);
        var version = WorkflowSupport.Clean(command.Version);
        foreach (var environmentId in environmentIds)
        {
            var deployment = new Deployment
            {
                IssueId = issue.Id,
                EnvironmentId = environmentId,
                DeployedById = workflow.UserId,
                DeployedAt = workflow.Now,
                Note = note,
                Version = version,
                RetestResult = RetestResult.Pending,
            };
            db.Deployments.Add(deployment);
            db.IssueActivities.Add(new IssueActivity
            {
                IssueId = issue.Id,
                UserId = workflow.UserId,
                Kind = ActivityKind.Deployed,
                Body = note,
                NewValue = version,
                EnvironmentId = environmentId,
                Deployment = deployment,
                CreatedAt = workflow.Now,
            });
        }

        var from = issue.Status;
        issue.Status = IssueWorkflow.TargetOf(from, IssueAction.MarkFixed);
        issue.FirstFixedAt ??= workflow.Now;
        issue.VerifiedAt = null;
        workflow.StatusChanged(issue, from, null);

        return await workflow.SaveAndReloadAsync(issue, command.RowVersion, cancellationToken);
    }
}

/// <summary>
/// Retest a deployment (plan 7.4): passed → Verified once no other retest is pending; failed → Reopened (reason
/// required, ReopenCount + 1, other pending retests of the issue fail with it).
/// </summary>
public sealed class RetestHandler(IValidator<RetestCommand> validator, IApplicationDbContext db, WorkflowSupport workflow)
{
    public async Task<Result<IssueDto>> HandleAsync(RetestCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var deployment = await db.Deployments.SingleOrDefaultAsync(d => d.Id == command.DeploymentId, cancellationToken);
        if (deployment is null)
        {
            return Error.NotFound("Deployment");
        }

        if (deployment.RetestResult != RetestResult.Pending)
        {
            return Error.Rule("already_retested", "This deployment was already retested.");
        }

        var issue = await db.Issues.Include(i => i.Sprint).Include(i => i.Project).SingleAsync(i => i.Id == deployment.IssueId, cancellationToken);
        var action = command.Passed ? IssueAction.Verify : IssueAction.Reopen;
        var check = await workflow.CheckAsync(issue, action, cancellationToken);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        workflow.Retested(deployment, command.Passed, command.Comment);
        var otherPending = (await workflow.PendingDeploymentsAsync(issue.Id, cancellationToken)).Where(d => d.Id != deployment.Id).ToList();
        var from = issue.Status;

        if (command.Passed)
        {
            if (otherPending.Count == 0)
            {
                issue.Status = IssueWorkflow.TargetOf(from, IssueAction.Verify);
                issue.VerifiedAt = workflow.Now;
            }
        }
        else
        {
            foreach (var other in otherPending)
            {
                workflow.Retested(other, passed: false, command.Comment);
            }

            issue.Status = IssueWorkflow.TargetOf(from, IssueAction.Reopen);
            issue.ReopenCount++;
        }

        workflow.StatusChanged(issue, from, command.Passed ? null : command.Comment);
        return await workflow.SaveAndReloadAsync(issue, rowVersion: null, cancellationToken);
    }
}

/// <summary>Bulk assign from the issue list (plan 7.3): each issue is checked on its own; the rest still go through.</summary>
public sealed class BulkAssignHandler(IValidator<BulkAssignCommand> validator, IApplicationDbContext db, WorkflowSupport workflow)
{
    public async Task<Result<BulkAssignResult>> HandleAsync(BulkAssignCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (await workflow.CheckAssigneeAsync(command.UserId, cancellationToken) is { } badAssignee)
        {
            return badAssignee;
        }

        var keys = command.Keys!.Select(IssueRules.NormalizeKey).Distinct().ToList();
        var issues = await db.Issues.Include(i => i.Sprint).Include(i => i.Project)
            .Where(i => keys.Contains(i.IssueKey))
            .ToDictionaryAsync(i => i.IssueKey, cancellationToken);

        var assigned = new List<string>();
        var failed = new List<BulkAssignFailure>();
        foreach (var key in keys)
        {
            if (!issues.TryGetValue(key, out var issue))
            {
                failed.Add(new BulkAssignFailure(key, "not_found", "Issue was not found."));
                continue;
            }

            var check = await workflow.CheckAsync(issue, IssueAction.Assign, cancellationToken);
            if (!check.Allowed)
            {
                failed.Add(new BulkAssignFailure(key, check.Code!, check.Reason!));
                continue;
            }

            if (issue.AssigneeId == command.UserId)
            {
                assigned.Add(key); // already theirs – nothing to change
                continue;
            }

            var from = issue.Status;
            workflow.Assigned(issue, issue.AssigneeId, command.UserId, command.Note);
            issue.AssigneeId = command.UserId;
            issue.Status = IssueWorkflow.TargetOf(from, IssueAction.Assign);
            issue.UpdatedAt = workflow.Now;
            workflow.StatusChanged(issue, from, null);
            assigned.Add(key);
        }

        if (await db.TrySaveAsync("One of the issues", cancellationToken) is { } saveError)
        {
            return saveError;
        }

        return new BulkAssignResult(assigned, failed);
    }
}
