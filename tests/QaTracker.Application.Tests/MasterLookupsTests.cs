using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Projects;

namespace QaTracker.Application.Tests;

public sealed class MasterLookupsTests
{
    private static SimilarName Existing(int id, string name) => new(id, name, false, true);

    [Fact]
    public void Similarity_FindsExactIgnoringCaseAndPunctuation_First()
    {
        var ranked = NameSimilarity.Rank("sales and distribution", [
            Existing(1, "Kaizen"),
            Existing(2, "Sales & Distribution"),
            Existing(3, "Sales and Distributon"),
        ]).ToList();

        ranked.Select(r => r.Id).ShouldBe([3, 2]);
        ranked.ShouldAllBe(r => !r.Exact);

        var exact = NameSimilarity.Rank("SALES-AND distribution", [Existing(3, "Sales and Distribution")]).Single();
        exact.Exact.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Sales Return", "Sales Returns", true)] // typo / plural
    [InlineData("BOM", "Item Mast, BOM, Tax Str.Mast", true)] // contained
    [InlineData("WDC", "Kaizen", false)]
    [InlineData("UAT 1", "UAT 2", true)] // one character apart – worth a warning
    public void Similarity_Matches(string typed, string existing, bool similar) =>
        NameSimilarity.Rank(typed, [Existing(1, existing)]).Any().ShouldBe(similar);

    [Theory]
    [InlineData("Pardi Gears", "ERP", "PGERP")]
    [InlineData("Pardi Gears", "Sales Portal", "PGSP")]
    [InlineData("Acme", "Inventory", "AINVEN")]
    [InlineData("123 Industries", "Web Shop", "IWS")]
    [InlineData("Very Long Client Name Ltd", "Enterprise Resource Planning System", "VLCERPS")]
    public void ProjectKey_BaseKey(string client, string project, string expected)
    {
        var key = ProjectKeySuggester.BaseKey(client, project);

        key.ShouldBe(expected);
        ProjectKeys.IsValid(key).ShouldBeTrue();
    }

    [Fact]
    public void ProjectKey_FirstFree_AddsNumberWithinEightCharacters()
    {
        ProjectKeySuggester.FirstFree("PGERP", new HashSet<string>()).ShouldBe("PGERP");
        ProjectKeySuggester.FirstFree("PGERP", new HashSet<string> { "PGERP", "PGERP2" }).ShouldBe("PGERP3");
        ProjectKeySuggester.FirstFree("ABCDEFGH", new HashSet<string> { "ABCDEFGH" }).ShouldBe("ABCDEFG2");
    }

    [Theory]
    [InlineData("PGERP", true)]
    [InlineData("P1", true)]
    [InlineData("A", false)]
    [InlineData("1PG", false)]
    [InlineData("PG-ERP", false)]
    [InlineData("ABCDEFGHI", false)]
    public void ProjectKey_Validation(string key, bool valid) => ProjectKeys.IsValid(key).ShouldBe(valid);
}
