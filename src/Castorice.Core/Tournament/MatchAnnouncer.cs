using System.Globalization;

namespace Castorice.Core.Tournament;

/// <summary>Where a match stands after a map was scored.</summary>
public sealed record MatchStanding(int RedScore, int BlueScore, int PointsToWin, string RedName, string BlueName)
{
    public TeamColour? MatchWinner => RedScore >= PointsToWin
        ? TeamColour.Red
        : BlueScore >= PointsToWin ? TeamColour.Blue : null;
}

/// <summary>
/// Formats the lines posted into the lobby after a map. Kept free of I/O so the exact wording can
/// be asserted — these go out in front of both teams.
/// </summary>
public static class MatchAnnouncer
{
    /// <summary>
    /// One to three lines: the map result, the multiplier adjustments if any were applied, and the
    /// running match score.
    /// </summary>
    public static IReadOnlyList<string> BuildResultMessages(
        MapResult result,
        string slotLabel,
        string mapName,
        MatchStanding standing)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(standing);

        var messages = new List<string>(3);
        var prefix = slotLabel.Length > 0 ? $"[{slotLabel}] " : string.Empty;
        var map = mapName.Length > 0 ? mapName : "the map";

        if (!result.HasScores)
        {
            return messages;
        }

        if (result.HasTeams)
        {
            var header =
                $"{prefix}{map} | {standing.RedName} {Number(result.RedTotal)} - " +
                $"{Number(result.BlueTotal)} {standing.BlueName}";

            messages.Add(result.Winner is { } winner
                ? $"{header} | {TeamName(winner, standing)} wins by {Number(result.Margin)}"
                : $"{header} | tied");
        }
        else
        {
            // Head to head: no teams to total, so report the ranking instead.
            var ranking = result.Players
                .Where(p => p.RawScore > 0)
                .OrderByDescending(p => p.AdjustedScore)
                .Take(5)
                .Select((p, i) => $"{i + 1}. {p.Username} {Number(p.AdjustedScore)}");

            messages.Add($"{prefix}{map} | {string.Join(" · ", ranking)}");
        }

        var adjusted = result.Adjusted.ToList();
        if (adjusted.Count > 0)
        {
            var parts = adjusted.Select(p =>
                $"{p.Username} {ModLabel(p.Mods)} x{p.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"({Number(p.RawScore)} -> {Number(p.AdjustedScore)})");

            messages.Add("Multipliers: " + string.Join(" · ", parts));
        }

        if (result.HasTeams)
        {
            messages.Add(standing.MatchWinner is { } matchWinner
                ? $"{TeamName(matchWinner, standing)} wins the match {standing.RedScore} - {standing.BlueScore}"
                : $"Match score: {standing.RedName} {standing.RedScore} - {standing.BlueScore} {standing.BlueName} " +
                  $"(first to {standing.PointsToWin})");
        }

        return messages;
    }

    /// <summary>The Easy part of a player's mods, which is the only part a multiplier reacts to.</summary>
    private static string ModLabel(Mods mods) =>
        mods.HasFlag(Mods.Easy) && mods.HasFlag(Mods.Hidden) ? "EZHD"
        : mods.HasFlag(Mods.Easy) ? "EZ"
        : mods.ToCompactAcronyms();

    private static string TeamName(TeamColour team, MatchStanding standing) =>
        team is TeamColour.Red ? standing.RedName : standing.BlueName;

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
