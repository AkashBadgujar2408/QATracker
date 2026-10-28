using QaTracker.Application.Features.Timeline;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Tests;

public sealed class TimelineTests
{
    [Theory]
    [InlineData("@nitin.dev please check", new[] { "nitin.dev" })]
    [InlineData("Ping @akash.tester and @Nitin.Dev.", new[] { "akash.tester", "Nitin.Dev" })]
    [InlineData("(@rahul_k) is next, @rahul_k again", new[] { "rahul_k" })]
    [InlineData("mail john@example.com, not a mention", new string[0])]
    [InlineData("@ab is too short", new string[0])]
    [InlineData("", new string[0])]
    public void MentionParser_FindsUserNames(string text, string[] expected) =>
        MentionParser.UserNames(text).ShouldBe(expected);

    [Fact]
    public void MentionParser_IgnoresCaseWhenDeduplicating() =>
        MentionParser.UserNames("@Emily.Dev and @emily.dev").Count.ShouldBe(1);

    [Fact]
    public void CommentRules_AllowTheAuthorToEditForTenMinutes()
    {
        var created = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

        CommentRules.CanEdit(5, 5, created, created.AddMinutes(10)).ShouldBeTrue();
        CommentRules.CanEdit(5, 5, created, created.AddMinutes(10).AddSeconds(1)).ShouldBeFalse();
        CommentRules.CanEdit(5, 6, created, created.AddMinutes(1)).ShouldBeFalse();
    }

    private static readonly Dictionary<int, string> Names = new() { [3] = "Nitin", [4] = "Rahul" };

    [Theory]
    [InlineData(ActivityKind.Assigned, 1, null, "3", "Akash assigned to Nitin")]
    [InlineData(ActivityKind.Assigned, 3, null, "3", "Akash picked the issue")]
    [InlineData(ActivityKind.Assigned, 1, "3", "4", "Akash reassigned from Nitin to Rahul")]
    [InlineData(ActivityKind.Assigned, 1, "3", null, "Akash unassigned Nitin")]
    [InlineData(ActivityKind.StatusChanged, 1, "NeedInfo", "InProgress", "Akash resumed work")]
    [InlineData(ActivityKind.StatusChanged, 1, "Disputed", "Assigned", "Akash disagreed – back to the developer")]
    [InlineData(ActivityKind.StatusChanged, 1, "Fixed", "Reopened", "Akash reopened the issue")]
    [InlineData(ActivityKind.Created, 1, null, null, "Akash reported the issue")]
    public void Summary_ReadsLikeTheTimeline(ActivityKind kind, int actorId, string? oldValue, string? newValue, string expected)
    {
        // The actor is "Akash" in every case; actorId 3 makes the assignment a pick (assigned to oneself).
        TimelineSummary.For(kind, "Akash", actorId, null, oldValue, newValue, null, Names).ShouldBe(expected);
    }

    [Fact]
    public void Summary_NamesTheEnvironment_ForDeploymentsAndRetests()
    {
        TimelineSummary.For(ActivityKind.Deployed, "Nitin", 3, "min=0", null, "1.4.2", "UAT 1", Names).ShouldBe("Nitin deployed the fix on UAT 1 (version 1.4.2)");
        TimelineSummary.For(ActivityKind.RetestFailed, "Akash", 1, "still -5", null, null, "UAT 1", Names).ShouldBe("Akash: retest failed on UAT 1");
        TimelineSummary.For(ActivityKind.Comment, "Akash", 1, "hello", null, null, null, Names).ShouldBeNull();
    }
}
