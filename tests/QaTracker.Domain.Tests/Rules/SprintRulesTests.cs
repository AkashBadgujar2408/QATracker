using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Domain.Tests.Rules;

public sealed class SprintRulesTests
{
    public static TheoryData<UserRole, SprintStatus, bool> AllRolesAndStatuses()
    {
        var data = new TheoryData<UserRole, SprintStatus, bool>();
        foreach (var role in Enum.GetValues<UserRole>())
        {
            foreach (var status in Enum.GetValues<SprintStatus>())
            {
                data.Add(role, status, false);
            }
        }

        return data;
    }

    [Theory]
    [InlineData(UserRole.Tester, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Developer, false)]
    public void Start_IsForTestersAndAdmins(UserRole role, bool allowed) =>
        SprintRules.CanStart(role, projectIsActive: true, openTestingSprintName: null).Allowed.ShouldBe(allowed);

    [Fact]
    public void Start_IsRefused_WhileAnotherSprintIsInTesting()
    {
        var check = SprintRules.CanStart(UserRole.Tester, true, "Sprint 1 – UAT");

        check.Allowed.ShouldBeFalse();
        check.Code.ShouldBe("sprint_already_testing");
        check.Reason!.ShouldContain("Sprint 1 – UAT");
    }

    [Fact]
    public void Start_IsRefused_ForAnInactiveProject() =>
        SprintRules.CanStart(UserRole.Admin, false, null).Code.ShouldBe("project_inactive");

    [Theory]
    [MemberData(nameof(AllRolesAndStatuses))]
    public void CompleteTesting_OnlyFromTesting_ByTesterOrAdmin(UserRole role, SprintStatus status, bool _)
    {
        var expected = role != UserRole.Developer && status == SprintStatus.Testing;
        SprintRules.CanCompleteTesting(role, status).Allowed.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(AllRolesAndStatuses))]
    public void ReopenTesting_OnlyFromTestingCompleted_ByAdmin(UserRole role, SprintStatus status, bool _)
    {
        var expected = role == UserRole.Admin && status == SprintStatus.TestingCompleted;
        SprintRules.CanReopenTesting(role, status, otherTestingSprintName: null).Allowed.ShouldBe(expected);
    }

    [Fact]
    public void ReopenTesting_IsRefused_WhenTheProjectHasAnotherSprintInTesting() =>
        SprintRules.CanReopenTesting(UserRole.Admin, SprintStatus.TestingCompleted, "Sprint 2 – UAT").Code
            .ShouldBe("sprint_already_testing");

    [Theory]
    [MemberData(nameof(AllRolesAndStatuses))]
    public void Close_OnlyFromTestingCompleted_ByTesterOrAdmin(UserRole role, SprintStatus status, bool _)
    {
        var expected = role != UserRole.Developer && status == SprintStatus.TestingCompleted;
        SprintRules.CanClose(role, status, unfinishedIssues: 0).Allowed.ShouldBe(expected);
    }

    [Fact]
    public void Close_IsRefused_WhileIssuesAreOpen() =>
        SprintRules.CanClose(UserRole.Tester, SprintStatus.TestingCompleted, 3).Code.ShouldBe("issues_unfinished");

    [Theory]
    [MemberData(nameof(AllRolesAndStatuses))]
    public void CarryForward_FromACompletedSprint_ByTesterOrAdmin(UserRole role, SprintStatus status, bool _)
    {
        var expected = role != UserRole.Developer && status == SprintStatus.TestingCompleted;
        SprintRules.CanCarryForward(role, 1, 10, status, (2, 10, SprintStatus.Testing)).Allowed.ShouldBe(expected);
    }

    [Theory]
    [InlineData(2, 10, SprintStatus.Testing, null)]
    [InlineData(2, 10, SprintStatus.TestingCompleted, null)]
    [InlineData(1, 10, SprintStatus.TestingCompleted, "same_sprint")]
    [InlineData(2, 11, SprintStatus.Testing, "other_project")]
    [InlineData(2, 10, SprintStatus.Closed, "target_closed")]
    public void CarryForward_GoesToAnotherOpenSprintOfTheProject(int targetId, int targetProjectId, SprintStatus targetStatus, string? code)
    {
        var check = SprintRules.CanCarryForward(UserRole.Tester, 1, 10, SprintStatus.TestingCompleted, (targetId, targetProjectId, targetStatus));
        check.Allowed.ShouldBe(code is null);
        check.Code.ShouldBe(code);
    }

    [Theory]
    [MemberData(nameof(AllRolesAndStatuses))]
    public void IssueChanges_OnlyWhileTesting_ByTesterOrAdmin(UserRole role, SprintStatus status, bool _)
    {
        var expected = role != UserRole.Developer && status == SprintStatus.Testing;
        SprintRules.CanChangeIssues(role, status).Allowed.ShouldBe(expected);
    }

    [Theory]
    [InlineData(IssueStatus.Closed, true)]
    [InlineData(IssueStatus.Rejected, true)]
    [InlineData(IssueStatus.Verified, false)]
    [InlineData(IssueStatus.New, false)]
    public void Finished_MeansClosedOrRejected(IssueStatus status, bool finished) =>
        SprintRules.IsFinished(status).ShouldBe(finished);
}
