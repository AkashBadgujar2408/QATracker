using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using static QaTracker.Domain.Enums.IssueStatus;

namespace QaTracker.Domain.Tests.Rules;

public sealed class IssueWorkflowTests
{
    private const int Me = 10;
    private const int Someone = 20;

    /// <summary>Who holds the issue relative to the acting user.</summary>
    public enum Holder
    {
        Nobody,
        Me,
        Someone,
    }

    /// <summary>
    /// Plan 7.2 written out independently of the implementation: who (T = tester, D = developer; admins always),
    /// from which statuses, to which status, and the assignee condition for developers.
    /// </summary>
    private sealed record Spec(string Who, IssueStatus[] From, IssueStatus? To, Func<UserRole, Holder, bool> Assignee);

    private static readonly Func<UserRole, Holder, bool> AnyHolder = (_, _) => true;
    private static readonly Func<UserRole, Holder, bool> DeveloperHolds = (role, holder) => role != UserRole.Developer || holder == Holder.Me;

    private static readonly Dictionary<IssueAction, Spec> Table = new()
    {
        [IssueAction.Assign] = new("TD", [New, Assigned, InProgress, NeedInfo, OnHold, Disputed, Fixed, Verified, Reopened], null, AnyHolder),
        [IssueAction.Pick] = new("D", [New, Assigned, Reopened], InProgress, (_, holder) => holder == Holder.Nobody),
        [IssueAction.Start] = new("D", [Assigned, Reopened], InProgress, (_, holder) => holder == Holder.Me),
        [IssueAction.NeedInfo] = new("D", [Assigned, InProgress], NeedInfo, DeveloperHolds),
        [IssueAction.Resume] = new("D", [NeedInfo], InProgress, DeveloperHolds),
        [IssueAction.Hold] = new("TD", [Assigned, InProgress], OnHold, DeveloperHolds),
        [IssueAction.Unhold] = new("TD", [OnHold], Assigned, DeveloperHolds),
        [IssueAction.Dispute] = new("D", [Assigned, InProgress], Disputed, DeveloperHolds),
        [IssueAction.Reject] = new("T", [Disputed], Rejected, AnyHolder),
        [IssueAction.Contest] = new("T", [Disputed], Assigned, AnyHolder),
        [IssueAction.MarkFixed] = new("D", [InProgress, Fixed, Verified], Fixed, DeveloperHolds),
        [IssueAction.Verify] = new("T", [Fixed], Verified, AnyHolder),
        [IssueAction.Reopen] = new("T", [Fixed, Closed], Reopened, AnyHolder),
        [IssueAction.Close] = new("T", [Verified], IssueStatus.Closed, AnyHolder),
    };

    private static WorkflowContext Context(IssueStatus status, SprintStatus sprint, UserRole role, Holder holder) =>
        new(status, sprint, holder switch { Holder.Me => Me, Holder.Someone => Someone, _ => null }, role, Me);

    private static bool Expected(IssueAction action, IssueStatus status, SprintStatus sprint, UserRole role, Holder holder)
    {
        var spec = Table[action];
        var roleOk = role == UserRole.Admin || spec.Who.Contains(role == UserRole.Tester ? 'T' : 'D');
        var sprintOk = sprint == SprintStatus.TestingCompleted
            || (sprint == SprintStatus.Testing && action == IssueAction.Assign && role != UserRole.Developer);
        return roleOk && sprintOk && spec.From.Contains(status) && spec.Assignee(role, holder);
    }

    [Fact]
    public void Table_CoversEveryAction() =>
        Table.Keys.Order().ShouldBe(IssueWorkflow.AllActions.Order());

    [Fact]
    public void EveryCombination_MatchesThePlanTable()
    {
        var mismatches = new List<string>();
        var cases = 0;
        foreach (var action in IssueWorkflow.AllActions)
        foreach (var status in Enum.GetValues<IssueStatus>())
        foreach (var sprint in Enum.GetValues<SprintStatus>())
        foreach (var role in Enum.GetValues<UserRole>())
        foreach (var holder in Enum.GetValues<Holder>())
        {
            cases++;
            var context = Context(status, sprint, role, holder);
            var check = IssueWorkflow.Check(context, action);
            var expected = Expected(action, status, sprint, role, holder);
            if (check.Allowed != expected)
            {
                mismatches.Add($"{action} from {status} in {sprint} by {role} (holder {holder}): expected {expected}, got {check.Allowed} ({check.Code})");
            }

            if (!check.Allowed && (string.IsNullOrWhiteSpace(check.Code) || string.IsNullOrWhiteSpace(check.Reason)))
            {
                mismatches.Add($"{action} from {status} in {sprint} by {role}: denial without code/reason");
            }

            // AllowedActions agrees with Check.
            if (IssueWorkflow.AllowedActions(context).Any(t => t.Action == action) != check.Allowed)
            {
                mismatches.Add($"{action} from {status} in {sprint} by {role} (holder {holder}): AllowedActions disagrees");
            }
        }

        cases.ShouldBe(14 * 11 * 3 * 3 * 3);
        mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void EveryAllowedTransition_LeadsToThePlannedStatus()
    {
        foreach (var (action, spec) in Table)
        foreach (var from in spec.From)
        {
            var expected = spec.To ?? (from == New ? Assigned : from);
            IssueWorkflow.TargetOf(from, action).ShouldBe(expected, $"{action} from {from}");

            var transition = IssueWorkflow.Transition(Context(from, SprintStatus.TestingCompleted, UserRole.Admin, Holder.Nobody), action);
            if (transition is not null)
            {
                transition.Target.ShouldBe(expected);
                transition.Inputs.ShouldBe(IssueWorkflow.InputsOf(action));
            }
        }
    }

    [Theory]
    [InlineData(IssueAction.Assign, ActionInput.Assignee)]
    [InlineData(IssueAction.Pick, ActionInput.None)]
    [InlineData(IssueAction.NeedInfo, ActionInput.Reason)]
    [InlineData(IssueAction.Hold, ActionInput.Reason)]
    [InlineData(IssueAction.Dispute, ActionInput.Reason)]
    [InlineData(IssueAction.Reject, ActionInput.RejectType | ActionInput.Reason)]
    [InlineData(IssueAction.Contest, ActionInput.Reason)]
    [InlineData(IssueAction.MarkFixed, ActionInput.Reason | ActionInput.Environments)]
    [InlineData(IssueAction.Reopen, ActionInput.Reason)]
    [InlineData(IssueAction.Verify, ActionInput.None)]
    [InlineData(IssueAction.Close, ActionInput.None)]
    public void Inputs_FollowThePlan(IssueAction action, ActionInput inputs) =>
        IssueWorkflow.InputsOf(action).ShouldBe(inputs);

    [Fact]
    public void Developer_IsBlocked_WhileTheSprintIsInTesting()
    {
        var check = IssueWorkflow.Check(Context(New, SprintStatus.Testing, UserRole.Developer, Holder.Nobody), IssueAction.Pick);

        check.Code.ShouldBe("sprint_in_testing");
        check.Reason!.ShouldContain("marks testing completed");
    }

    [Fact]
    public void Tester_CanPreAssign_WhileTheSprintIsInTesting()
    {
        var context = Context(New, SprintStatus.Testing, UserRole.Tester, Holder.Nobody);

        IssueWorkflow.AllowedActions(context).Select(t => t.Action).ShouldBe([IssueAction.Assign]);
        IssueWorkflow.Transition(context, IssueAction.Assign)!.Target.ShouldBe(Assigned);
    }

    [Fact]
    public void ClosedSprint_IsReadOnly_EvenForAdmins()
    {
        var context = Context(Verified, SprintStatus.Closed, UserRole.Admin, Holder.Me);

        IssueWorkflow.AllowedActions(context).ShouldBeEmpty();
        IssueWorkflow.Check(context, IssueAction.Close).Code.ShouldBe("sprint_closed");
    }

    [Fact]
    public void Pick_ExplainsWhoHasTheIssue()
    {
        IssueWorkflow.Check(Context(Assigned, SprintStatus.TestingCompleted, UserRole.Developer, Holder.Someone), IssueAction.Pick)
            .Code.ShouldBe("already_assigned");
        IssueWorkflow.Check(Context(Assigned, SprintStatus.TestingCompleted, UserRole.Developer, Holder.Me), IssueAction.Pick)
            .Reason!.ShouldContain("start it instead");
    }

    [Fact]
    public void Developer_MustBeTheAssignee_ToMarkFixed()
    {
        var check = IssueWorkflow.Check(Context(InProgress, SprintStatus.TestingCompleted, UserRole.Developer, Holder.Someone), IssueAction.MarkFixed);

        check.Code.ShouldBe("not_assignee");
    }

    [Fact]
    public void Admin_MayActForTheAssignee() =>
        IssueWorkflow.Check(Context(InProgress, SprintStatus.TestingCompleted, UserRole.Admin, Holder.Someone), IssueAction.MarkFixed)
            .Allowed.ShouldBeTrue();

    [Fact]
    public void WrongRole_IsReportedBeforeTheStatus()
    {
        var check = IssueWorkflow.Check(Context(New, SprintStatus.TestingCompleted, UserRole.Developer, Holder.Me), IssueAction.Verify);

        check.Code.ShouldBe("role_not_allowed");
        check.Reason.ShouldBe("Only testers and admins can verify fixes.");
    }

    [Fact]
    public void InvalidTransition_NamesTheStatus()
    {
        var check = IssueWorkflow.Check(Context(NeedInfo, SprintStatus.TestingCompleted, UserRole.Tester, Holder.Nobody), IssueAction.Close);

        check.Code.ShouldBe("invalid_transition");
        check.Reason.ShouldBe("Closing is not possible while the issue is waiting for information.");
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Close_NeedsAPassedLiveRetest_WhenTheProjectRequiresIt(bool required, bool passed, bool allowed)
    {
        var context = Context(Verified, SprintStatus.TestingCompleted, UserRole.Tester, Holder.Someone) with
        {
            RequireLiveVerification = required,
            HasPassedLiveRetest = passed,
        };

        var check = IssueWorkflow.Check(context, IssueAction.Close);

        check.Allowed.ShouldBe(allowed);
        if (!allowed)
        {
            check.Code.ShouldBe("live_verification_required");
        }
    }

    [Fact]
    public void AllowedActions_ForTheAssignedDeveloper_InProgress()
    {
        var context = Context(InProgress, SprintStatus.TestingCompleted, UserRole.Developer, Holder.Me);

        IssueWorkflow.AllowedActions(context).Select(t => t.Action).ShouldBe(
        [
            IssueAction.Assign, IssueAction.NeedInfo, IssueAction.Hold, IssueAction.Dispute, IssueAction.MarkFixed,
        ]);
    }

    [Fact]
    public void AllowedActions_ForTheTester_OnAFixedIssue()
    {
        var context = Context(Fixed, SprintStatus.TestingCompleted, UserRole.Tester, Holder.Someone);

        IssueWorkflow.AllowedActions(context).Select(t => t.Action).ShouldBe(
        [
            IssueAction.Assign, IssueAction.Verify, IssueAction.Reopen,
        ]);
    }

    [Fact]
    public void FromStatuses_AreTheTableRows_UsedByTheWorkQueues()
    {
        IssueWorkflow.FromStatuses(IssueAction.Pick).ShouldBe([New, Assigned, Reopened]);
        IssueWorkflow.FromStatuses(IssueAction.MarkFixed).ShouldBe([InProgress, Fixed, Verified]);
    }
}
