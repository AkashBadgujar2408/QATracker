using QaTracker.Domain.Rules;

namespace QaTracker.Domain.Tests.Rules;

public sealed class SprintNamesTests
{
    [Fact]
    public void FirstSprint_IsSprintOne() => SprintNames.Suggest([], "Early Testing").ShouldBe("Sprint 1 – Early Testing");

    [Fact]
    public void NewPhase_KeepsTheLatestSprintNumber() =>
        SprintNames.Suggest(["Sprint 1 – Early Testing"], "UAT").ShouldBe("Sprint 1 – UAT");

    [Fact]
    public void LatestNumber_ComesFromTheNewestSprint() =>
        SprintNames.Suggest(["Sprint 2 – Early Testing", "Sprint 1 – LIVE", "Sprint 1 – UAT"], "UAT").ShouldBe("Sprint 2 – UAT");

    [Fact]
    public void TakenName_GetsARound() =>
        SprintNames.Suggest(["Sprint 1 – UAT Round 2", "sprint 1 – uat"], "UAT").ShouldBe("Sprint 1 – UAT Round 3");

    [Fact]
    public void FreeFormNames_AreSkippedWhenFindingTheNumber() =>
        SprintNames.Suggest(["Hotfix retest", "Sprint 3 – UAT"], "LIVE").ShouldBe("Sprint 3 – LIVE");
}
