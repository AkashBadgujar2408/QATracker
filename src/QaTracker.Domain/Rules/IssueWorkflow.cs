using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Rules;

/// <summary>Something a user can do with an issue's workflow (plan 7.2). Not stored – the timeline records the outcome.</summary>
public enum IssueAction
{
    /// <summary>Assign or reassign to someone (New → Assigned, otherwise the status stays).</summary>
    Assign,
    /// <summary>Developer takes an unassigned issue and starts it (atomic self-assign).</summary>
    Pick,
    /// <summary>The assignee starts work.</summary>
    Start,
    /// <summary>Ask the reporter a question.</summary>
    NeedInfo,
    /// <summary>Back to work after the question was answered.</summary>
    Resume,
    Hold,
    /// <summary>Take an issue off hold (back to Assigned).</summary>
    Unhold,
    /// <summary>Developer says "not a bug".</summary>
    Dispute,
    /// <summary>Tester agrees with the dispute.</summary>
    Reject,
    /// <summary>Tester disagrees with the dispute – back to the developer.</summary>
    Contest,
    /// <summary>Fixed with one or more deployments; from Verified (or Fixed) it adds a new deployment, e.g. LIVE.</summary>
    MarkFixed,
    /// <summary>Retest passed.</summary>
    Verify,
    /// <summary>Retest failed, or a closed issue regressed. Reopen count + 1.</summary>
    Reopen,
    Close,
}

/// <summary>What an action needs from the user besides the click (the API validates these).</summary>
[Flags]
public enum ActionInput
{
    None = 0,
    Assignee = 1,
    /// <summary>A reason, question or comment.</summary>
    Reason = 2,
    RejectType = 4,
    /// <summary>One or more environments of the project (deployments).</summary>
    Environments = 8,
}

/// <param name="Status">Current issue status.</param>
/// <param name="SprintStatus">State of the issue's sprint.</param>
/// <param name="AssigneeId">Current assignee, if any.</param>
/// <param name="Role">Role of the acting user.</param>
/// <param name="UserId">The acting user.</param>
/// <param name="RequireLiveVerification">Project setting: closing needs a passed LIVE retest.</param>
/// <param name="HasPassedLiveRetest">The issue has a deployment on a LIVE environment whose retest passed.</param>
public readonly record struct WorkflowContext(
    IssueStatus Status,
    SprintStatus SprintStatus,
    int? AssigneeId,
    UserRole Role,
    int UserId,
    bool RequireLiveVerification = false,
    bool HasPassedLiveRetest = false);

/// <summary>An allowed action, where it leads and what it needs.</summary>
public sealed record WorkflowTransition(IssueAction Action, IssueStatus Target, ActionInput Inputs);

/// <summary>
/// The one place for issue transition rules (plan 7.2). Admins may do everything a tester or developer may do.
/// Checks run in a fixed order – sprint closed, role, sprint state, status, assignee, project setting – so the reason
/// is the most useful one. Codes: <c>sprint_closed</c>, <c>role_not_allowed</c>, <c>sprint_in_testing</c>,
/// <c>invalid_transition</c>, <c>not_assignee</c>, <c>already_assigned</c>, <c>live_verification_required</c>.
/// </summary>
public static class IssueWorkflow
{
    private enum Actor
    {
        Tester,
        Developer,
        Both,
    }

    private enum AssigneeRule
    {
        Any,
        /// <summary>Developers must be the assignee (testers and admins are not limited).</summary>
        DeveloperMustBeAssignee,
        Unassigned,
        AssignedToMe,
    }

    private sealed record Rule(
        Actor Actor,
        IReadOnlyList<IssueStatus> From,
        IssueStatus? Target,
        ActionInput Inputs,
        AssigneeRule Assignee = AssigneeRule.Any);

    /// <summary>Statuses in which an issue can still be (re)assigned.</summary>
    private static readonly IssueStatus[] Assignable =
    [
        IssueStatus.New, IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.NeedInfo, IssueStatus.OnHold,
        IssueStatus.Disputed, IssueStatus.Fixed, IssueStatus.Verified, IssueStatus.Reopened,
    ];

    /// <summary>Plan 7.2 table. A null target means "status unchanged" (only Assign, except New → Assigned).</summary>
    private static readonly IReadOnlyDictionary<IssueAction, Rule> Rules = new Dictionary<IssueAction, Rule>
    {
        [IssueAction.Assign] = new(Actor.Both, Assignable, null, ActionInput.Assignee),
        [IssueAction.Pick] = new(Actor.Developer, [IssueStatus.New, IssueStatus.Assigned, IssueStatus.Reopened], IssueStatus.InProgress, ActionInput.None, AssigneeRule.Unassigned),
        [IssueAction.Start] = new(Actor.Developer, [IssueStatus.Assigned, IssueStatus.Reopened], IssueStatus.InProgress, ActionInput.None, AssigneeRule.AssignedToMe),
        [IssueAction.NeedInfo] = new(Actor.Developer, [IssueStatus.Assigned, IssueStatus.InProgress], IssueStatus.NeedInfo, ActionInput.Reason, AssigneeRule.DeveloperMustBeAssignee),
        [IssueAction.Resume] = new(Actor.Developer, [IssueStatus.NeedInfo], IssueStatus.InProgress, ActionInput.None, AssigneeRule.DeveloperMustBeAssignee),
        [IssueAction.Hold] = new(Actor.Both, [IssueStatus.Assigned, IssueStatus.InProgress], IssueStatus.OnHold, ActionInput.Reason, AssigneeRule.DeveloperMustBeAssignee),
        [IssueAction.Unhold] = new(Actor.Both, [IssueStatus.OnHold], IssueStatus.Assigned, ActionInput.None, AssigneeRule.DeveloperMustBeAssignee),
        [IssueAction.Dispute] = new(Actor.Developer, [IssueStatus.Assigned, IssueStatus.InProgress], IssueStatus.Disputed, ActionInput.Reason, AssigneeRule.DeveloperMustBeAssignee),
        [IssueAction.Reject] = new(Actor.Tester, [IssueStatus.Disputed], IssueStatus.Rejected, ActionInput.RejectType | ActionInput.Reason),
        [IssueAction.Contest] = new(Actor.Tester, [IssueStatus.Disputed], IssueStatus.Assigned, ActionInput.Reason),
        [IssueAction.MarkFixed] = new(Actor.Developer, [IssueStatus.InProgress, IssueStatus.Fixed, IssueStatus.Verified], IssueStatus.Fixed, ActionInput.Reason | ActionInput.Environments, AssigneeRule.DeveloperMustBeAssignee),
        [IssueAction.Verify] = new(Actor.Tester, [IssueStatus.Fixed], IssueStatus.Verified, ActionInput.None),
        [IssueAction.Reopen] = new(Actor.Tester, [IssueStatus.Fixed, IssueStatus.Closed], IssueStatus.Reopened, ActionInput.Reason),
        [IssueAction.Close] = new(Actor.Tester, [IssueStatus.Verified], IssueStatus.Closed, ActionInput.None),
    };

    public static IReadOnlyList<IssueAction> AllActions { get; } = Enum.GetValues<IssueAction>();

    /// <summary>May the user take this action now?</summary>
    public static RuleCheck Check(WorkflowContext context, IssueAction action)
    {
        var rule = Rules[action];

        if (context.SprintStatus == SprintStatus.Closed)
        {
            return RuleCheck.Deny("sprint_closed", "The sprint is closed – its issues are read-only.");
        }

        if (!RoleMayAct(context.Role, rule.Actor))
        {
            return RuleCheck.Deny("role_not_allowed", RoleReason(action, rule.Actor));
        }

        // During testing only the tester's (pre-)assignment is possible; developers wait for Testing Completed.
        var testerPreAssigns = action == IssueAction.Assign && context.Role != UserRole.Developer;
        if (context.SprintStatus == SprintStatus.Testing && !testerPreAssigns)
        {
            return RuleCheck.Deny(
                "sprint_in_testing",
                "Testing is in progress. Developers can act on issues once the tester marks testing completed.");
        }

        if (!rule.From.Contains(context.Status))
        {
            return RuleCheck.Deny("invalid_transition", $"{Describe(action)} is not possible while the issue is {Label(context.Status)}.");
        }

        var assigneeCheck = CheckAssignee(context, rule.Assignee);
        if (!assigneeCheck.Allowed)
        {
            return assigneeCheck;
        }

        if (action == IssueAction.Close && context.RequireLiveVerification && !context.HasPassedLiveRetest)
        {
            return RuleCheck.Deny(
                "live_verification_required",
                "This project needs a passed retest on a LIVE environment before an issue can be closed.");
        }

        return RuleCheck.Ok;
    }

    /// <summary>The transition for an allowed action, or null when it is not allowed.</summary>
    public static WorkflowTransition? Transition(WorkflowContext context, IssueAction action)
    {
        if (!Check(context, action).Allowed)
        {
            return null;
        }

        var rule = Rules[action];
        return new WorkflowTransition(action, TargetOf(context.Status, action, rule), rule.Inputs);
    }

    /// <summary>Everything the user may do now, in a stable order (the API's <c>allowedActions</c>).</summary>
    public static IReadOnlyList<WorkflowTransition> AllowedActions(WorkflowContext context) =>
        AllActions.Select(action => Transition(context, action)).OfType<WorkflowTransition>().ToList();

    /// <summary>Status after the action (Assign keeps the status, except New → Assigned).</summary>
    public static IssueStatus TargetOf(IssueStatus from, IssueAction action) => TargetOf(from, action, Rules[action]);

    /// <summary>What the action needs from the user.</summary>
    public static ActionInput InputsOf(IssueAction action) => Rules[action].Inputs;

    /// <summary>Statuses the action can start from (work queues: issues open to pick, fixes to announce).</summary>
    public static IReadOnlyList<IssueStatus> FromStatuses(IssueAction action) => Rules[action].From;

    private static IssueStatus TargetOf(IssueStatus from, IssueAction action, Rule rule) =>
        rule.Target ?? (action == IssueAction.Assign && from == IssueStatus.New ? IssueStatus.Assigned : from);

    private static bool RoleMayAct(UserRole role, Actor actor) => role switch
    {
        UserRole.Admin => true,
        UserRole.Tester => actor is Actor.Tester or Actor.Both,
        UserRole.Developer => actor is Actor.Developer or Actor.Both,
        _ => false,
    };

    private static RuleCheck CheckAssignee(WorkflowContext context, AssigneeRule rule) => rule switch
    {
        AssigneeRule.Unassigned when context.AssigneeId is not null => context.AssigneeId == context.UserId
            ? RuleCheck.Deny("already_assigned", "The issue is already assigned to you – start it instead.")
            : RuleCheck.Deny("already_assigned", "Someone else already has this issue. Ask them, or reassign it."),
        AssigneeRule.AssignedToMe when context.AssigneeId != context.UserId => context.AssigneeId is null
            ? RuleCheck.Deny("not_assignee", "Nobody is assigned yet – pick the issue instead.")
            : RuleCheck.Deny("not_assignee", "Only the assignee can start this issue."),
        AssigneeRule.DeveloperMustBeAssignee when context.Role == UserRole.Developer && context.AssigneeId != context.UserId =>
            RuleCheck.Deny("not_assignee", "Only the assigned developer can do this. Pick or reassign the issue first."),
        _ => RuleCheck.Ok,
    };

    private static string RoleReason(IssueAction action, Actor actor) => actor == Actor.Tester
        ? $"Only testers and admins can {Verb(action)}."
        : $"Only developers and admins can {Verb(action)}.";

    private static string Verb(IssueAction action) => action switch
    {
        IssueAction.Pick => "pick issues",
        IssueAction.Start => "start work on issues",
        IssueAction.NeedInfo => "ask for information",
        IssueAction.Resume => "resume work",
        IssueAction.Dispute => "dispute issues",
        IssueAction.MarkFixed => "mark issues fixed",
        IssueAction.Reject => "reject issues",
        IssueAction.Contest => "send a disputed issue back",
        IssueAction.Verify => "verify fixes",
        IssueAction.Reopen => "reopen issues",
        IssueAction.Close => "close issues",
        _ => "do this",
    };

    private static string Describe(IssueAction action) => action switch
    {
        IssueAction.Assign => "Assigning",
        IssueAction.Pick => "Picking",
        IssueAction.Start => "Starting",
        IssueAction.NeedInfo => "Asking for information",
        IssueAction.Resume => "Resuming",
        IssueAction.Hold => "Putting on hold",
        IssueAction.Unhold => "Taking off hold",
        IssueAction.Dispute => "Disputing",
        IssueAction.Reject => "Rejecting",
        IssueAction.Contest => "Sending back",
        IssueAction.MarkFixed => "Marking fixed",
        IssueAction.Verify => "Verifying",
        IssueAction.Reopen => "Reopening",
        IssueAction.Close => "Closing",
        _ => action.ToString(),
    };

    private static string Label(IssueStatus status) => status switch
    {
        IssueStatus.InProgress => "In Progress",
        IssueStatus.NeedInfo => "waiting for information",
        IssueStatus.OnHold => "on hold",
        _ => status.ToString(),
    };
}
