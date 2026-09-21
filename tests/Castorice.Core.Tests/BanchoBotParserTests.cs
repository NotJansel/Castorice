using Castorice.Core.Bancho;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class BanchoBotParserTests
{
    [Fact]
    public void Recognises_a_created_match()
    {
        var evt = Assert.IsType<MatchCreated>(
            BanchoBotParser.Parse("Created the tournament match https://osu.ppy.sh/mp/104507864 OWC: (Red) vs (Blue)"));

        Assert.Equal(104507864, evt.MatchId);
        Assert.Equal("OWC: (Red) vs (Blue)", evt.RoomName);
        Assert.Equal("#mp_104507864", evt.ChannelName);
    }

    [Fact]
    public void Recognises_the_settings_header()
    {
        var evt = Assert.IsType<RoomHeader>(
            BanchoBotParser.Parse("Room name: CAST: (Alpha) vs (Beta), History: https://osu.ppy.sh/mp/999"));

        Assert.Equal("CAST: (Alpha) vs (Beta)", evt.RoomName);
        Assert.Equal(999, evt.MatchId);
    }

    [Theory]
    [InlineData("Beatmap: https://osu.ppy.sh/b/2087 Artist - Title [Insane]")]
    [InlineData("Changed beatmap to https://osu.ppy.sh/b/2087 Artist - Title [Insane]")]
    public void Recognises_a_beatmap_line(string line)
    {
        var evt = Assert.IsType<BeatmapChanged>(BanchoBotParser.Parse(line));

        Assert.Equal(2087, evt.BeatmapId);
        Assert.Equal("Artist", evt.Artist);
        Assert.Equal("Title", evt.Title);
        Assert.Equal("Insane", evt.Difficulty);
    }

    [Fact]
    public void Splits_a_beatmap_name_containing_hyphens()
    {
        var (artist, title, difficulty) =
            BanchoBotParser.SplitBeatmapName("Some - Band - A Song - Remix [Extra - Hard]");

        Assert.Equal("Some", artist);
        Assert.Equal("Band - A Song - Remix", title);
        Assert.Equal("Extra - Hard", difficulty);
    }

    [Fact]
    public void Recognises_active_mods_in_both_spellings()
    {
        // !mp settings prints full names; !mp mods echoes acronyms.
        var full = Assert.IsType<ModsChanged>(BanchoBotParser.Parse("Active mods: Hidden, HardRock"));
        Assert.Equal(Mods.Hidden | Mods.HardRock, full.Mods);

        var acronyms = Assert.IsType<ModsChanged>(BanchoBotParser.Parse("Active mods: HD, HR"));
        Assert.Equal(Mods.Hidden | Mods.HardRock, acronyms.Mods);
    }

    [Fact]
    public void Recognises_changed_match_settings()
    {
        var evt = Assert.IsType<MatchSettingsChanged>(
            BanchoBotParser.Parse("Changed match settings to 16 slots, TeamVs, ScoreV2"));

        Assert.Equal(TeamMode.TeamVs, evt.TeamMode);
        Assert.Equal(ScoreMode.ScoreV2, evt.ScoreMode);
        Assert.Equal(16, evt.Size);
    }

    [Fact]
    public void Recognises_the_team_mode_line_from_settings()
    {
        var evt = Assert.IsType<MatchSettingsChanged>(
            BanchoBotParser.Parse("Team mode: TeamVs, Win condition: ScoreV2"));

        Assert.Equal(TeamMode.TeamVs, evt.TeamMode);
        Assert.Equal(ScoreMode.ScoreV2, evt.ScoreMode);
        Assert.Null(evt.Size);
    }

    [Fact]
    public void Recognises_a_slot_row()
    {
        var evt = Assert.IsType<SlotReport>(
            BanchoBotParser.Parse("Slot 1  Not Ready https://osu.ppy.sh/u/1234 Some Player    [Team Blue / HardRock]"));

        Assert.Equal(1, evt.Slot);
        Assert.Equal("Not Ready", evt.Status);
        Assert.Equal(1234, evt.UserId);
        Assert.Equal("Some Player", evt.Username);
        Assert.Equal(TeamColour.Blue, evt.Team);
    }

    [Fact]
    public void Recognises_a_slot_row_without_a_team()
    {
        var evt = Assert.IsType<SlotReport>(
            BanchoBotParser.Parse("Slot 3  Ready    https://osu.ppy.sh/u/7 Solo"));

        Assert.Equal(3, evt.Slot);
        Assert.Equal("Solo", evt.Username);
        Assert.Null(evt.Team);
    }

    [Fact]
    public void Recognises_players_joining_and_leaving()
    {
        var joined = Assert.IsType<PlayerJoined>(BanchoBotParser.Parse("Some Player joined in slot 4 for team red."));
        Assert.Equal("Some Player", joined.Username);
        Assert.Equal(4, joined.Slot);
        Assert.Equal(TeamColour.Red, joined.Team);

        var plain = Assert.IsType<PlayerJoined>(BanchoBotParser.Parse("Other joined in slot 2."));
        Assert.Null(plain.Team);

        var left = Assert.IsType<PlayerLeft>(BanchoBotParser.Parse("Some Player left the game."));
        Assert.Equal("Some Player", left.Username);
    }

    [Fact]
    public void Recognises_a_finished_score()
    {
        var evt = Assert.IsType<PlayerScore>(
            BanchoBotParser.Parse("Some Player finished playing (Score: 812345, PASSED)."));

        Assert.Equal("Some Player", evt.Username);
        Assert.Equal(812345, evt.Score);
        Assert.True(evt.Passed);

        var failed = Assert.IsType<PlayerScore>(BanchoBotParser.Parse("X finished playing (Score: 1, FAILED)."));
        Assert.False(failed.Passed);
    }

    [Theory]
    [InlineData("The match has started!", typeof(MatchStarted))]
    [InlineData("The match has finished!", typeof(MatchFinished))]
    [InlineData("Aborted the match", typeof(MatchAborted))]
    [InlineData("Closed the match", typeof(MatchClosed))]
    [InlineData("All players are ready", typeof(AllPlayersReady))]
    [InlineData("Countdown finished", typeof(CountdownFinished))]
    public void Recognises_the_fixed_lines(string line, Type expected)
    {
        Assert.IsType(expected, BanchoBotParser.Parse(line));
    }

    [Theory]
    [InlineData("just some chat")]
    [InlineData("")]
    [InlineData(null)]
    public void Returns_null_for_anything_else(string? line) => Assert.Null(BanchoBotParser.Parse(line));
}
