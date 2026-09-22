using Castorice.Core.Bancho;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

/// <summary>
/// The parser checked against what BanchoBot actually said during a real bracket match, rather
/// than against what its formats were assumed to be. The fixture holds every distinct BanchoBot
/// line from <c>osu.ppy.sh/mp/120020454</c>.
/// </summary>
public class RealMatchLogTests
{
    private static IReadOnlyList<string> Lines { get; } = File
        .ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bancho-lines.txt"))
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .ToList();

    private static T ParseAs<T>(string line) where T : BanchoEvent =>
        Assert.IsType<T>(BanchoBotParser.Parse(line));

    [Fact]
    public void The_fixture_is_actually_there()
    {
        Assert.True(Lines.Count > 100, $"expected the real log's lines, got {Lines.Count}");
    }

    [Fact]
    public void Reads_a_room_name_that_itself_contains_a_colon_and_brackets()
    {
        var evt = ParseAs<RoomHeader>(
            "Room name: FDC2: (pisyukastyye) vs (FA Team 6), History: https://osu.ppy.sh/mp/120020454");

        Assert.Equal("FDC2: (pisyukastyye) vs (FA Team 6)", evt.RoomName);
        Assert.Equal(120020454, evt.MatchId);
    }

    [Theory]
    // A slot with no mods prints the team with a trailing space and no separator at all.
    [InlineData("Slot 1  Ready     https://osu.ppy.sh/u/22185509 igoryaka        [Team Red ]",
        1, "igoryaka", 22185509, TeamColour.Red, Mods.None)]
    [InlineData("Slot 3  Ready     https://osu.ppy.sh/u/35162776 Kal3ber         [Team Blue]",
        3, "Kal3ber", 35162776, TeamColour.Blue, Mods.None)]
    [InlineData("Slot 1  Ready     https://osu.ppy.sh/u/22185509 igoryaka        [Team Red  / NoFail, Hidden]",
        1, "igoryaka", 22185509, TeamColour.Red, Mods.NoFail | Mods.Hidden)]
    [InlineData("Slot 2  Ready     https://osu.ppy.sh/u/33976861 Polistin        [Team Red  / NoFail, Hidden, HardRock]",
        2, "Polistin", 33976861, TeamColour.Red, Mods.NoFail | Mods.Hidden | Mods.HardRock)]
    public void Reads_the_slot_rows_bancho_really_prints(
        string line, int slot, string user, long userId, TeamColour team, Mods mods)
    {
        var evt = ParseAs<SlotReport>(line);

        Assert.Equal(slot, evt.Slot);
        Assert.Equal(user, evt.Username);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal(team, evt.Team);
        Assert.Equal(mods, evt.Mods);
    }

    [Fact]
    public void Reads_a_finished_score()
    {
        var evt = ParseAs<PlayerScore>("Kal3ber finished playing (Score: 1112977, PASSED).");

        Assert.Equal("Kal3ber", evt.Username);
        Assert.Equal(1112977, evt.Score);
        Assert.True(evt.Passed);
    }

    [Theory]
    [InlineData("Enabled NoFail, disabled FreeMod", Mods.NoFail)]
    [InlineData("Enabled NoFail, DoubleTime, disabled FreeMod", Mods.NoFail | Mods.DoubleTime)]
    [InlineData("Enabled NoFail, HardRock, disabled FreeMod", Mods.NoFail | Mods.HardRock)]
    [InlineData("Disabled all mods, enabled FreeMod", Mods.FreeMod)]
    [InlineData("Active mods: NoFail, Easy", Mods.NoFail | Mods.Easy)]
    [InlineData("Active mods: Freemod", Mods.FreeMod)]
    public void Reads_both_ways_bancho_reports_mods(string line, Mods expected) =>
        Assert.Equal(expected, ParseAs<ModsChanged>(line).Mods);

    [Fact]
    public void Reads_a_beatmap_line_that_carries_no_difficulty_name()
    {
        // The real lines stop at the title; the difficulty is simply absent.
        var evt = ParseAs<BeatmapChanged>(
            "Beatmap: https://osu.ppy.sh/b/4753271 cassie - me & u (succducc bootleg) (Kara Edit)");

        Assert.Equal(4753271, evt.BeatmapId);
        Assert.Equal("cassie", evt.Artist);
        Assert.Equal("me & u (succducc bootleg) (Kara Edit)", evt.Title);
        Assert.Equal(string.Empty, evt.Difficulty);
    }

    [Fact]
    public void Reads_a_move_line_which_carries_no_full_stop()
    {
        var evt = ParseAs<PlayerMoved>("igoryaka moved to slot 3");

        Assert.Equal("igoryaka", evt.Username);
        Assert.Equal(3, evt.Slot);
    }

    [Fact]
    public void Reads_the_match_settings_line_with_a_slot_count()
    {
        var evt = ParseAs<MatchSettingsChanged>("Changed match settings to 7 slots, TeamVs, ScoreV2");

        Assert.Equal(TeamMode.TeamVs, evt.TeamMode);
        Assert.Equal(ScoreMode.ScoreV2, evt.ScoreMode);
        Assert.Equal(7, evt.Size);
    }

    [Fact]
    public void Every_line_that_carries_room_state_is_understood()
    {
        // Anything Bancho says that changes the room has to parse. The rest — invites, rolls,
        // countdowns, "Good luck, have fun!" — is chatter and is meant to fall through to chat.
        string[] chatterPrefixes =
        [
            "Players:", "Invited ", "Countdown ends in", "Countdown aborted", "Match starts in",
            "Queued the match", "Added ", "Good luck", "Changed match mode",
        ];

        var unparsed = Lines
            .Where(l => BanchoBotParser.Parse(l) is null)
            .Where(l => !chatterPrefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal)))
            .Where(l => !l.Contains(" rolls ", StringComparison.Ordinal))
            .Where(l => !l.Contains(" is in ", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(unparsed);
    }

    [Fact]
    public void The_real_match_satisfies_the_default_freemod_quota()
    {
        // Red fielded HD and HDHR, Blue HR and HD — one of each group per side.
        var players = Lines
            .Select(BanchoBotParser.Parse)
            .OfType<SlotReport>()
            .Where(s => s.Mods is not Mods.None)
            .Select(s => new PlayerScoreInput(s.Username, s.Team, 0, true, s.Mods))
            .ToList();

        Assert.Equal(4, players.Count);
        Assert.True(FreeModCheck.Check(players).IsClean, FreeModCheck.Check(players).Summary);
    }
}
