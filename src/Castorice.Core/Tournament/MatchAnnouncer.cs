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
    /// <param name="announcements">Which of the lines to include; <c>null</c> includes all three.</param>
    public static IReadOnlyList<string> BuildResultMessages(
        MapResult result,
        string slotLabel,
        string mapName,
        MatchStanding standing,
        LobbyAnnouncements? announcements = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(standing);

        var include = announcements ?? AllLines;

        var messages = new List<string>(3);
        var prefix = slotLabel.Length > 0 ? $"[{slotLabel}] " : string.Empty;
        var map = mapName.Length > 0 ? mapName : "the map";

        if (!result.HasScores)
        {
            return messages;
        }

        if (include.MapResult)
        {
            messages.Add(MapResultLine(result, prefix + map, standing));
        }

        var adjusted = result.Adjusted.ToList();
        if (include.Multipliers && adjusted.Count > 0)
        {
            var parts = adjusted.Select(p =>
                $"{p.Username} {ModLabel(p.Mods)} x{p.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"({Number(p.RawScore)} -> {Number(p.AdjustedScore)})");

            messages.Add("Multipliers: " + string.Join(" · ", parts));
        }

        if (include.MatchScore && result.HasTeams)
        {
            messages.Add(MatchScoreLine(standing));
        }

        return messages;
    }

    private static string MapResultLine(MapResult result, string map, MatchStanding standing)
    {
        if (!result.HasTeams)
        {
            // Head to head: no teams to total, so report the ranking instead.
            var ranking = result.Players
                .Where(p => p.RawScore > 0)
                .OrderByDescending(p => p.AdjustedScore)
                .Take(5)
                .Select((p, i) => $"{i + 1}. {p.Username} {Number(p.AdjustedScore)}");

            return $"{map} | {string.Join(" · ", ranking)}";
        }

        var header =
            $"{map} | {standing.RedName} {Number(result.RedTotal)} - " +
            $"{Number(result.BlueTotal)} {standing.BlueName}";

        return result.Winner is { } winner
            ? $"{header} | {TeamName(winner, standing)} wins by {Number(result.Margin)}"
            : $"{header} | tied";
    }

    /// <summary>The running score, or the winner once a team has reached the target.</summary>
    public static string MatchScoreLine(MatchStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return standing.MatchWinner is { } matchWinner
            ? $"{TeamName(matchWinner, standing)} wins the match {standing.RedScore} - {standing.BlueScore}"
            : $"Match score: {standing.RedName} {standing.RedScore} - {standing.BlueScore} {standing.BlueName} " +
              $"(first to {standing.PointsToWin})";
    }

    /// <summary>
    /// A protect, ban or pick as it happens, e.g. <c>Red bans NM2</c> or
    /// <c>Blue picks DT1: Artist - Title [Diff]</c>. A tiebreaker pick has no team.
    /// </summary>
    public static string DraftActionLine(DraftPhase phase, TeamColour? team, string slotLabel, string mapName, TeamNames names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var map = mapName.Length > 0 ? $"{slotLabel}: {mapName}" : slotLabel;

        return phase switch
        {
            DraftPhase.Protect => $"{names.For(team)} protects {slotLabel}",
            DraftPhase.Ban => $"{names.For(team)} bans {slotLabel}",
            DraftPhase.Pick when team is not null => $"{names.For(team)} picks {map}",
            _ => $"Tiebreaker: {(mapName.Length > 0 ? mapName : slotLabel)}",
        };
    }

    /// <summary>A team passing on its protect, which some brackets allow.</summary>
    public static string ProtectSkippedLine(TeamColour team, TeamNames names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return $"{names.For(team)} skips their protect";
    }

    /// <summary>
    /// Whose turn it is, e.g. <c>Next: Blue bans (2/4)</c>. <c>null</c> when there is nothing
    /// worth saying: the match is over, or the next picker hangs on a map still being played.
    /// </summary>
    public static string? NextTurnLine(DraftTurn turn, TeamNames names)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(names);

        var count = turn.Total > 0 ? $" ({turn.Number}/{turn.Total})" : string.Empty;
        var round = turn.Round > 1 ? " (second round)" : string.Empty;

        return turn.Phase switch
        {
            DraftPhase.Protect => $"Next: {names.For(turn.Team)} protects{count}",
            DraftPhase.Ban => $"Next: {names.For(turn.Team)} bans{round}{count}",
            DraftPhase.Pick when turn.Team is not null => $"Next: {names.For(turn.Team)} picks",
            DraftPhase.Tiebreaker => "Next: Tiebreaker",
            _ => null,
        };
    }

    /// <summary>
    /// Every protect, ban and pick so far on one line, e.g.
    /// <c>Protects: Red HD1, Blue DT1 | Bans: Red NM2, Blue HR1 | Picks: FM1 (Red)</c>.
    /// </summary>
    public static string DraftSummaryLine(
        IEnumerable<(string Label, SlotAvailability Mark)> marks,
        IEnumerable<(string Label, TeamColour? Team)> picks,
        TeamNames names,
        IEnumerable<TeamColour>? skippedProtects = null)
    {
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(names);

        var markList = marks.ToList();
        var parts = new List<string>(3);

        var protects = markList
            .Where(m => m.Mark.IsProtected())
            .Select(m => $"{names.For(m.Mark.Team())} {m.Label}")
            .Concat((skippedProtects ?? []).Select(team => $"{names.For(team)} skipped"))
            .ToList();
        if (protects.Count > 0)
        {
            parts.Add("Protects: " + string.Join(", ", protects));
        }

        var bans = markList.Where(m => m.Mark.IsBanned()).ToList();
        if (bans.Count > 0)
        {
            parts.Add("Bans: " + string.Join(", ", bans.Select(m => $"{names.For(m.Mark.Team())} {m.Label}")));
        }

        var pickList = picks.ToList();
        if (pickList.Count > 0)
        {
            parts.Add("Picks: " + string.Join(", ", pickList.Select(p =>
                p.Team is null ? p.Label : $"{p.Label} ({names.For(p.Team)})")));
        }

        return parts.Count == 0 ? "No protects, bans or picks yet." : string.Join(" | ", parts);
    }

    private static readonly LobbyAnnouncements AllLines = new();

    /// <summary>The Easy part of a player's mods, which is the only part a multiplier reacts to.</summary>
    private static string ModLabel(Mods mods) =>
        mods.HasFlag(Mods.Easy) && mods.HasFlag(Mods.Hidden) ? "EZHD"
        : mods.HasFlag(Mods.Easy) ? "EZ"
        : mods.ToCompactAcronyms();

    private static string TeamName(TeamColour team, MatchStanding standing) =>
        team is TeamColour.Red ? standing.RedName : standing.BlueName;

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
