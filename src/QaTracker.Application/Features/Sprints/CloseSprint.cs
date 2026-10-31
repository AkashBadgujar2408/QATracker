using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Sprints;

/// <summary>An issue to move, with the version the caller saw (409 when it changed meanwhile).</summary>
public sealed record IssueVersion(int Id, string RowVersion);

/// <param name="Note">Shown in both sprint histories and on each issue's timeline.</param>
public sealed record CarryForwardCommand(int SprintId, int TargetSprintId, IReadOnlyList<IssueVersion> Issues, string? Note = null);

/// <summary>Both sprints after the move.</summary>
public sealed record CarryForwardResult(SprintDto Sprint, SprintDto Target, int CarriedCount);

/// <param name="CarryForward">Open issues to carry forward in the same transaction, before closing.</param>
public sealed record CloseSprintCommand(int SprintId, string RowVersion, string? Note = null, CarryForwardPart? CarryForward = null);

public sealed record CarryForwardPart(int TargetSprintId, IReadOnlyList<IssueVersion> Issues);

public sealed class CarryForwardValidator : AbstractValidator<CarryForwardCommand>
{
    public CarryForwardValidator()
    {
        RuleFor(x => x.Issues).Cascade(CascadeMode.Stop).NotEmpty().Must(CarryForwardRules.Distinct).WithMessage("Each issue can be listed once.")
            .Must(i => i.Count <= CarryForwardRules.MaxIssues).WithMessage($"At most {CarryForwardRules.MaxIssues} issues at a time.");
        RuleForEach(x => x.Issues).ChildRules(i => i.RuleFor(x => x.RowVersion).ValidRowVersion());
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class CloseSprintValidator : AbstractValidator<CloseSprintCommand>
{
    public CloseSprintValidator()
    {
        RuleFor(x => x.RowVersion).ValidRowVersion();
        RuleFor(x => x.Note).MaximumLength(1000);
        When(x => x.CarryForward is not null, () =>
        {
            RuleFor(x => x.CarryForward!.Issues).Cascade(CascadeMode.Stop).NotEmpty().Must(CarryForwardRules.Distinct).WithMessage("Each issue can be listed once.")
                .Must(i => i.Count <= CarryForwardRules.MaxIssues).WithMessage($"At most {CarryForwardRules.MaxIssues} issues at a time.");
            RuleForEach(x => x.CarryForward!.Issues).ChildRules(i => i.RuleFor(x => x.RowVersion).ValidRowVersion());
        });
    }
}

internal static class CarryForwardRules
{
    public const int MaxIssues = 1000;

    public static bool Distinct(IReadOnlyList<IssueVersion> issues) => issues.Select(i => i.Id).Distinct().Count() == issues.Count;
}

/// <summary>
/// Carry forward (plan 6.1, ADR-039): open issues of a completed sprint move to another open sprint of the project and
/// keep their key. The leaving sprint freezes each issue in its snapshot (with the sprint it went to), both sprint
/// histories and each issue's timeline record the move. Shared by "Carry forward" and "Close sprint".
/// </summary>
public sealed class CarryForwardStep(IApplicationDbContext db, ICurrentUser currentUser)
{
    /// <summary>Stages the move on the tracked context (nothing is saved). Returns the target sprint.</summary>
    public async Task<Result<TestingSprint>> StageAsync(
        TestingSprint source,
        int targetSprintId,
        IReadOnlyList<IssueVersion> requested,
        string? note,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = await db.TestingSprints.SingleOrDefaultAsync(s => s.Id == targetSprintId, cancellationToken);
        if (target is null)
        {
            return Error.NotFound("Target sprint");
        }

        var check = SprintRules.CanCarryForward(currentUser.Role, source.Id, source.ProjectId, source.Status, (target.Id, target.ProjectId, target.Status));
        if (!check.Allowed)
        {
            return check.ToError();
        }

        var ids = requested.Select(i => i.Id).ToList();
        var issues = await db.Issues.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        if (issues.Count != ids.Count || issues.Any(i => i.SprintId != source.Id))
        {
            return Error.Rule("issue_not_in_sprint", "Some of the chosen issues are no longer in this sprint. Reload and choose again.");
        }

        if (issues.FirstOrDefault(i => SprintRules.IsFinished(i.Status)) is { } finished)
        {
            return Error.Rule("issue_finished", $"{finished.IssueKey} is {finished.Status.ToString().ToLowerInvariant()} – only open issues are carried forward.");
        }

        var snapshots = await db.SprintSnapshots.Where(s => s.SprintId == source.Id && ids.Contains(s.IssueId)).ToDictionaryAsync(s => s.IssueId, cancellationToken);
        var cleanNote = SprintText.Clean(note);
        foreach (var issue in issues)
        {
            db.ExpectRowVersion(issue, requested.Single(r => r.Id == issue.Id).RowVersion);
            Freeze(snapshots.GetValueOrDefault(issue.Id), source.Id, issue, target.Id);

            issue.SprintId = target.Id;
            issue.CarriedFromSprintId = source.Id;
            // A sprint still in testing hands the issue to developers only when its testing completes – the due date
            // is set again then. Into a completed sprint the issue keeps working to its due date.
            if (target.Status == SprintStatus.Testing)
            {
                issue.DueDate = null;
            }

            issue.UpdatedAt = now;
            db.IssueActivities.Add(new IssueActivity
            {
                IssueId = issue.Id,
                UserId = currentUser.UserId,
                Kind = ActivityKind.CarriedForward,
                Body = cleanNote,
                OldValue = source.Name,
                NewValue = target.Name,
                CreatedAt = now,
            });
        }

        var keys = KeyList(issues);
        db.SprintEvents.Add(new SprintEvent
        {
            SprintId = source.Id,
            UserId = currentUser.UserId,
            Kind = SprintEventKind.IssuesCarriedForward,
            Note = Limit($"{Count(issues.Count)} carried forward to {target.Name}: {keys}." + (cleanNote is null ? "" : $" {cleanNote}")),
            At = now,
        });
        db.SprintEvents.Add(new SprintEvent
        {
            SprintId = target.Id,
            UserId = currentUser.UserId,
            Kind = SprintEventKind.IssuesCarriedIn,
            Note = Limit($"{Count(issues.Count)} carried forward from {source.Name}: {keys}." + (cleanNote is null ? "" : $" {cleanNote}")),
            At = now,
        });

        return target;
    }

    /// <summary>Snapshot row for an issue leaving (carriedTo) or staying in a sprint at closing (null).</summary>
    internal void Freeze(SprintSnapshot? existing, int sprintId, Issue issue, int? carriedTo)
    {
        var snapshot = existing ?? db.SprintSnapshots.Add(new SprintSnapshot { SprintId = sprintId, IssueId = issue.Id }).Entity;
        snapshot.StatusAtClose = issue.Status;
        snapshot.SeverityAtClose = issue.Severity;
        snapshot.AssigneeAtCloseId = issue.AssigneeId;
        snapshot.CarriedToSprintId = carriedTo;
    }

    private static string Count(int n) => n == 1 ? "1 issue" : $"{n} issues";

    /// <summary>"PGERP-4, PGERP-7 …" in number order, at most 20 keys.</summary>
    private static string KeyList(List<Issue> issues)
    {
        var keys = issues.OrderBy(i => i.Number).Select(i => i.IssueKey).ToList();
        return keys.Count <= 20 ? string.Join(", ", keys) : string.Join(", ", keys.Take(20)) + $" and {keys.Count - 20} more";
    }

    private static string Limit(string note) => note.Length <= 1000 ? note : note[..999] + "…";
}

public sealed class CarryForwardHandler(
    IValidator<CarryForwardCommand> validator,
    IApplicationDbContext db,
    CarryForwardStep step,
    IClock clock)
{
    public async Task<Result<CarryForwardResult>> HandleAsync(CarryForwardCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var sprint = await db.TestingSprints.SingleOrDefaultAsync(s => s.Id == command.SprintId, cancellationToken);
        if (sprint is null)
        {
            return Error.NotFound("Sprint");
        }

        var staged = await step.StageAsync(sprint, command.TargetSprintId, command.Issues, command.Note, clock.UtcNow, cancellationToken);
        if (!staged.IsSuccess)
        {
            return staged.Error;
        }

        if (await db.TrySaveAsync("One of the issues", cancellationToken) is { } failed)
        {
            return failed;
        }

        var both = await db.TestingSprints.AsNoTracking()
            .Where(s => s.Id == sprint.Id || s.Id == command.TargetSprintId)
            .Select(SprintDto.Projection)
            .ToListAsync(cancellationToken);
        return new CarryForwardResult(both.Single(s => s.Id == sprint.Id), both.Single(s => s.Id == command.TargetSprintId), command.Issues.Count);
    }
}

/// <summary>
/// "Close sprint" (plan 6): allowed when every issue is Closed or Rejected – optionally after carrying the remaining
/// ones forward in the same transaction. Freezes status, severity and assignee of every issue in SprintSnapshots so
/// the report keeps the numbers as of closing; the sprint becomes read-only.
/// </summary>
public sealed class CloseSprintHandler(
    IValidator<CloseSprintCommand> validator,
    IApplicationDbContext db,
    CarryForwardStep step,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<SprintDto>> HandleAsync(CloseSprintCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var sprint = await db.TestingSprints.SingleOrDefaultAsync(s => s.Id == command.SprintId, cancellationToken);
        if (sprint is null)
        {
            return Error.NotFound("Sprint");
        }

        var now = clock.UtcNow;
        var carried = new HashSet<int>();
        if (command.CarryForward is { } carry)
        {
            var staged = await step.StageAsync(sprint, carry.TargetSprintId, carry.Issues, command.Note, now, cancellationToken);
            if (!staged.IsSuccess)
            {
                return staged.Error;
            }

            carried = carry.Issues.Select(i => i.Id).ToHashSet();
        }

        // Tracked issues still in the sprint (the carried ones already point to their new sprint in memory).
        var issues = (await db.Issues.Where(i => i.SprintId == sprint.Id).ToListAsync(cancellationToken))
            .Where(i => !carried.Contains(i.Id))
            .ToList();
        var check = SprintRules.CanClose(currentUser.Role, sprint.Status, issues.Count(i => !SprintRules.IsFinished(i.Status)));
        if (!check.Allowed)
        {
            return check.ToError();
        }

        db.ExpectRowVersion(sprint, command.RowVersion);
        sprint.Status = SprintStatus.Closed;
        sprint.ClosedAt = now;
        sprint.ClosedById = currentUser.UserId;
        db.SprintEvents.Add(new SprintEvent
        {
            SprintId = sprint.Id,
            UserId = currentUser.UserId,
            Kind = SprintEventKind.Closed,
            Note = SprintText.Clean(command.Note),
            At = now,
        });

        var ids = issues.Select(i => i.Id).ToList();
        var snapshots = await db.SprintSnapshots.Where(s => s.SprintId == sprint.Id && ids.Contains(s.IssueId)).ToDictionaryAsync(s => s.IssueId, cancellationToken);
        foreach (var issue in issues)
        {
            step.Freeze(snapshots.GetValueOrDefault(issue.Id), sprint.Id, issue, carriedTo: null);
            db.IssueActivities.Add(new IssueActivity
            {
                IssueId = issue.Id,
                UserId = currentUser.UserId,
                Kind = ActivityKind.SprintEvent,
                Body = $"{sprint.Name} closed.",
                CreatedAt = now,
            });
        }

        if (await db.TrySaveAsync("This sprint", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.TestingSprints.AsNoTracking().Where(s => s.Id == sprint.Id).Select(SprintDto.Projection).SingleAsync(cancellationToken);
    }
}
