using System.Globalization;
using System.Text.RegularExpressions;

namespace QaTracker.Domain.Rules;

/// <summary>Suggested sprint names (plan 6.1): "Sprint 2 – UAT", or "Sprint 1 – UAT Round 2" when that name is taken.</summary>
public static partial class SprintNames
{
    /// <param name="existingNames">The project's sprint names, newest first; the newest "Sprint N" sets the number.</param>
    /// <param name="phase">Phase of the new sprint, e.g. "UAT".</param>
    public static string Suggest(IReadOnlyList<string> existingNames, string phase)
    {
        var latest = existingNames.Select(name => SprintNumber().Match(name)).FirstOrDefault(match => match.Success);
        var number = latest is null ? 1 : int.Parse(latest.Groups[1].Value, CultureInfo.InvariantCulture);

        var taken = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = $"Sprint {number} – {phase.Trim()}";
        if (!taken.Contains(name))
        {
            return name;
        }

        var round = 2;
        while (taken.Contains($"{name} Round {round}"))
        {
            round++;
        }

        return $"{name} Round {round}";
    }

    [GeneratedRegex(@"^\s*Sprint\s+(\d{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SprintNumber();
}
