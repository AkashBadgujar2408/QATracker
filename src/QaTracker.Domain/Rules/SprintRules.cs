using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Rules;

/// <summary>
/// Testing sprint lifecycle (plan 6): Testing → TestingCompleted → Closed, with an admin-only "reopen testing".
/// One sprint in Testing per project; issue details change only while the sprint is in Testing.
/// </summary>
public static class SprintRules
{
    /// <param name="openTestingSprintName">The project's sprint that is still in Testing, if any.</param>
    public static RuleCheck CanStart(UserRole role, bool projectIsActive, string? openTestingSprintName)
    {
        if (!IsTesterOrAdmin(role))
        {
            return RuleCheck.Deny("role_not_allowed", "Only testers and admins start testing sprints.");
        }

        if (!projectIsActive)
        {
            return RuleCheck.Deny("project_inactive", "The project is inactive – activate it before starting a sprint.");
        }

        return openTestingSprintName is null
            ? RuleCheck.Ok
            : RuleCheck.Deny(
                "sprint_already_testing",
                $"'{openTestingSprintName}' is still in testing. Mark it Testing Completed before starting a new sprint.");
    }

    public static RuleCheck CanCompleteTesting(UserRole role, SprintStatus status)
    {
        if (!IsTesterOrAdmin(role))
        {
            return RuleCheck.Deny("role_not_allowed", "Only testers and admins mark testing completed.");
        }

        return status == SprintStatus.Testing
            ? RuleCheck.Ok
            : RuleCheck.Deny("sprint_not_testing", "Testing of this sprint is already completed.");
    }

    /// <param name="otherTestingSprintName">Another sprint of the same project that is in Testing, if any.</param>
    public static RuleCheck CanReopenTesting(UserRole role, SprintStatus status, string? otherTestingSprintName)
    {
        if (role != UserRole.Admin)
        {
            return RuleCheck.Deny("role_not_allowed", "Only an admin can reopen testing.");
        }

        if (status != SprintStatus.TestingCompleted)
        {
            return RuleCheck.Deny(
                "sprint_not_testing_completed",
                status == SprintStatus.Testing ? "This sprint is still in testing." : "A closed sprint cannot be reopened.");
        }

        return otherTestingSprintName is null
            ? RuleCheck.Ok
            : RuleCheck.Deny(
                "sprint_already_testing",
                $"'{otherTestingSprintName}' is in testing for this project. Complete it before reopening another sprint.");
    }

    /// <param name="unfinishedIssues">Issues that are neither Closed nor Rejected (and were not carried forward).</param>
    public static RuleCheck CanClose(UserRole role, SprintStatus status, int unfinishedIssues)
    {
        if (!IsTesterOrAdmin(role))
        {
            return RuleCheck.Deny("role_not_allowed", "Only testers and admins close sprints.");
        }

        if (status != SprintStatus.TestingCompleted)
        {
            return RuleCheck.Deny(
                "sprint_not_testing_completed",
                status == SprintStatus.Testing ? "Mark testing completed before closing the sprint." : "The sprint is already closed.");
        }

        return unfinishedIssues == 0
            ? RuleCheck.Ok
            : RuleCheck.Deny(
                "issues_unfinished",
                $"{unfinishedIssues} issue(s) are still open. Close or reject them, or carry them forward to another sprint.");
    }

    /// <summary>
    /// Carry forward (plan 6.1): open issues of a completed sprint move to another sprint of the same project that is
    /// not closed. The issues keep their key.
    /// </summary>
    /// <param name="target">The sprint the issues go to: its project and state.</param>
    public static RuleCheck CanCarryForward(UserRole role, int sourceId, int sourceProjectId, SprintStatus sourceStatus, (int Id, int ProjectId, SprintStatus Status) target)
    {
        if (!IsTesterOrAdmin(role))
        {
            return RuleCheck.Deny("role_not_allowed", "Only testers and admins carry issues forward.");
        }

        if (sourceStatus != SprintStatus.TestingCompleted)
        {
            return RuleCheck.Deny(
                "sprint_not_testing_completed",
                sourceStatus == SprintStatus.Testing
                    ? "Mark testing completed before carrying issues forward."
                    : "The sprint is closed – its issues can no longer be carried forward.");
        }

        if (target.Id == sourceId)
        {
            return RuleCheck.Deny("same_sprint", "Choose another sprint to carry the issues to.");
        }

        if (target.ProjectId != sourceProjectId)
        {
            return RuleCheck.Deny("other_project", "Issues can only be carried forward to a sprint of the same project.");
        }

        return target.Status == SprintStatus.Closed
            ? RuleCheck.Deny("target_closed", "The chosen sprint is closed. Choose an open sprint or start a new one.")
            : RuleCheck.Ok;
    }

    /// <summary>Add, edit or delete issues (details, not workflow): tester/admin while the sprint is in Testing.</summary>
    public static RuleCheck CanChangeIssues(UserRole role, SprintStatus status)
    {
        if (!IsTesterOrAdmin(role))
        {
            return RuleCheck.Deny("role_not_allowed", "Only testers and admins add or edit issues.");
        }

        return status == SprintStatus.Testing
            ? RuleCheck.Ok
            : RuleCheck.Deny(
                "sprint_not_testing",
                "Testing of this sprint is completed, so issues can no longer be added or edited. Log new defects in a new sprint.");
    }

    /// <summary>Closed and Rejected issues need no more work.</summary>
    public static bool IsFinished(IssueStatus status) => status is IssueStatus.Closed or IssueStatus.Rejected;

    private static bool IsTesterOrAdmin(UserRole role) => role is UserRole.Tester or UserRole.Admin;
}
