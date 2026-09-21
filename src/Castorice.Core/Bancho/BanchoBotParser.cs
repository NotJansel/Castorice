using System.Globalization;
using System.Text.RegularExpressions;
using Castorice.Core.Tournament;

namespace Castorice.Core.Bancho;

/// <summary>
/// Recognises the BanchoBot lines that describe a multiplayer room. Anything unrecognised returns
/// <c>null</c> and is left to be shown as ordinary chat.
/// </summary>
public static partial class BanchoBotParser
{
    public static BanchoEvent? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Trim();

        if (MatchCreatedPattern().Match(line) is { Success: true } created)
        {
            return Tag(new MatchCreated(ParseId(created.Groups["id"].Value), created.Groups["name"].Value.Trim()), line);
        }

        if (RoomHeaderPattern().Match(line) is { Success: true } header)
        {
            return Tag(new RoomHeader(header.Groups["name"].Value.Trim(), ParseId(header.Groups["id"].Value)), line);
        }

        if (BeatmapPattern().Match(line) is { Success: true } beatmap)
        {
            var (artist, title, difficulty) = SplitBeatmapName(beatmap.Groups["name"].Value);
            return Tag(new BeatmapChanged(ParseId(beatmap.Groups["id"].Value), artist, title, difficulty), line);
        }

        if (ActiveModsPattern().Match(line) is { Success: true } mods)
        {
            return Tag(new ModsChanged(ModsExtensions.ParseMods(mods.Groups["mods"].Value)), line);
        }

        if (SettingsChangedPattern().Match(line) is { Success: true } settings)
        {
            var size = settings.Groups["size"].Success
                ? int.Parse(settings.Groups["size"].Value, CultureInfo.InvariantCulture)
                : (int?)null;

            return Tag(
                new MatchSettingsChanged(
                    ParseTeamMode(settings.Groups["team"].Value),
                    ParseScoreMode(settings.Groups["score"].Value),
                    size),
                line);
        }

        if (TeamModePattern().Match(line) is { Success: true } teamMode)
        {
            return Tag(
                new MatchSettingsChanged(
                    ParseTeamMode(teamMode.Groups["team"].Value),
                    ParseScoreMode(teamMode.Groups["score"].Value),
                    Size: null),
                line);
        }

        if (SlotRowPattern().Match(line) is { Success: true } slot)
        {
            return Tag(
                new SlotReport(
                    int.Parse(slot.Groups["slot"].Value, CultureInfo.InvariantCulture),
                    slot.Groups["status"].Value.Trim(),
                    ParseId(slot.Groups["userid"].Value),
                    slot.Groups["user"].Value.Trim(),
                    ParseTeam(slot.Groups["team"].Value),
                    ModsExtensions.ParseMods(slot.Groups["mods"].Value)),
                line);
        }

        if (JoinedPattern().Match(line) is { Success: true } joined)
        {
            return Tag(
                new PlayerJoined(
                    joined.Groups["user"].Value.Trim(),
                    int.Parse(joined.Groups["slot"].Value, CultureInfo.InvariantCulture),
                    ParseTeam(joined.Groups["team"].Value)),
                line);
        }

        if (LeftPattern().Match(line) is { Success: true } left)
        {
            return Tag(new PlayerLeft(left.Groups["user"].Value.Trim()), line);
        }

        if (MovedPattern().Match(line) is { Success: true } moved)
        {
            return Tag(
                new PlayerMoved(
                    moved.Groups["user"].Value.Trim(),
                    int.Parse(moved.Groups["slot"].Value, CultureInfo.InvariantCulture)),
                line);
        }

        if (TeamChangePattern().Match(line) is { Success: true } teamChange)
        {
            var team = ParseTeam(teamChange.Groups["team"].Value);
            if (team is not null)
            {
                return Tag(new PlayerTeamChanged(teamChange.Groups["user"].Value.Trim(), team.Value), line);
            }
        }

        if (ScorePattern().Match(line) is { Success: true } score)
        {
            return Tag(
                new PlayerScore(
                    score.Groups["user"].Value.Trim(),
                    long.Parse(score.Groups["score"].Value, CultureInfo.InvariantCulture),
                    score.Groups["result"].Value.Equals("PASSED", StringComparison.OrdinalIgnoreCase)),
                line);
        }

        if (HostPattern().Match(line) is { Success: true } host)
        {
            return Tag(new HostChanged(host.Groups["user"].Value.Trim()), line);
        }

        return line switch
        {
            "The match has started!" => Tag(new MatchStarted(), line),
            "The match has finished!" => Tag(new MatchFinished(), line),
            "Aborted the match" => Tag(new MatchAborted(), line),
            "Closed the match" => Tag(new MatchClosed(), line),
            "All players are ready" => Tag(new AllPlayersReady(), line),
            "Countdown finished" => Tag(new CountdownFinished(), line),
            _ => null,
        };
    }

    /// <summary>Splits <c>Artist - Title [Difficulty]</c>, tolerating hyphens inside either field.</summary>
    internal static (string Artist, string Title, string Difficulty) SplitBeatmapName(string name)
    {
        var trimmed = name.Trim();
        var difficulty = string.Empty;

        var open = trimmed.LastIndexOf('[');
        if (open > 0 && trimmed.EndsWith(']'))
        {
            difficulty = trimmed[(open + 1)..^1].Trim();
            trimmed = trimmed[..open].TrimEnd();
        }

        var separator = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return (string.Empty, trimmed, difficulty);
        }

        return (trimmed[..separator].Trim(), trimmed[(separator + 3)..].Trim(), difficulty);
    }

    private static BanchoEvent Tag(BanchoEvent evt, string raw) => evt with { Raw = raw };

    private static long ParseId(string value) =>
        long.TryParse(value, CultureInfo.InvariantCulture, out var id) ? id : 0;

    private static TeamColour? ParseTeam(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "red" => TeamColour.Red,
        "blue" => TeamColour.Blue,
        _ => null,
    };

    private static TeamMode ParseTeamMode(string value) => value.Replace(" ", string.Empty) switch
    {
        var v when v.Equals("TeamVs", StringComparison.OrdinalIgnoreCase) => TeamMode.TeamVs,
        var v when v.Equals("TagTeamVs", StringComparison.OrdinalIgnoreCase) => TeamMode.TagTeamVs,
        var v when v.Equals("TagCoop", StringComparison.OrdinalIgnoreCase) => TeamMode.TagCoop,
        _ => TeamMode.HeadToHead,
    };

    private static ScoreMode ParseScoreMode(string value) => value.Replace(" ", string.Empty) switch
    {
        var v when v.Equals("ScoreV2", StringComparison.OrdinalIgnoreCase) => ScoreMode.ScoreV2,
        var v when v.Equals("Accuracy", StringComparison.OrdinalIgnoreCase) => ScoreMode.Accuracy,
        var v when v.Equals("Combo", StringComparison.OrdinalIgnoreCase) => ScoreMode.Combo,
        _ => ScoreMode.Score,
    };

    [GeneratedRegex(@"^Created the tournament match https?://osu\.ppy\.sh/mp/(?<id>\d+)\s*(?<name>.*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex MatchCreatedPattern();

    [GeneratedRegex(@"^Room name:\s*(?<name>.*?),\s*History:\s*https?://osu\.ppy\.sh/mp/(?<id>\d+)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RoomHeaderPattern();

    [GeneratedRegex(@"^(?:Beatmap|Changed beatmap to):?\s*https?://osu\.ppy\.sh/b/(?<id>\d+)\s+(?<name>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BeatmapPattern();

    [GeneratedRegex(@"^Active mods:\s*(?<mods>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ActiveModsPattern();

    [GeneratedRegex(
        @"^Changed match settings to\s*(?:(?<size>\d+)\s*slots,?\s*)?(?<team>[A-Za-z ]+?),\s*(?<score>[A-Za-z0-9 ]+)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SettingsChangedPattern();

    [GeneratedRegex(@"^Team mode:\s*(?<team>[A-Za-z ]+?),\s*Win condition:\s*(?<score>[A-Za-z0-9 ]+)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TeamModePattern();

    [GeneratedRegex(
        @"^Slot\s+(?<slot>\d+)\s+(?<status>\S+(?:\s+\S+)?)\s+https?://osu\.ppy\.sh/u/(?<userid>\d+)\s+(?<user>.+?)\s*(?:\[\s*(?:Team\s+(?<team>Red|Blue))?\s*/?\s*(?<mods>[^\]]*?)\s*\])?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SlotRowPattern();

    [GeneratedRegex(@"^(?<user>.+?)\s+joined in slot\s+(?<slot>\d+)(?:\s+for team\s+(?<team>red|blue))?\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex JoinedPattern();

    [GeneratedRegex(@"^(?<user>.+?)\s+left the game\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex LeftPattern();

    [GeneratedRegex(@"^(?<user>.+?)\s+moved to slot\s+(?<slot>\d+)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex MovedPattern();

    [GeneratedRegex(@"^(?<user>.+?)\s+changed to\s+(?<team>Red|Blue)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex TeamChangePattern();

    [GeneratedRegex(
        @"^(?<user>.+?)\s+finished playing\s*\(Score:\s*(?<score>\d+),\s*(?<result>PASSED|FAILED)\)\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ScorePattern();

    [GeneratedRegex(@"^Changed match host to\s+(?<user>.+?)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex HostPattern();
}
