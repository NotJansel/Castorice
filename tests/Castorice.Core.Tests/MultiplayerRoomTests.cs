using Castorice.Core.Bancho;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class MultiplayerRoomTests
{
    [Fact]
    public void Builds_the_roster_from_slot_reports()
    {
        var room = new MultiplayerRoom(1);

        room.Apply("Slot 1  Not Ready https://osu.ppy.sh/u/1 Alpha [Team Red / HardRock]");
        room.Apply("Slot 2  Ready     https://osu.ppy.sh/u/2 Beta  [Team Blue]");

        Assert.Equal(2, room.Players.Count);

        var alpha = room.FindPlayer("Alpha");
        Assert.NotNull(alpha);
        Assert.Equal(TeamColour.Red, alpha.Team);
        Assert.Equal(Mods.HardRock, alpha.Mods);
    }

    [Fact]
    public void Tracks_joins_moves_and_leaves()
    {
        var room = new MultiplayerRoom(1);

        room.Apply("Some Player joined in slot 3 for team blue.");
        Assert.Equal(3, room.FindPlayer("Some Player")!.Slot);

        room.Apply("Some Player moved to slot 5.");
        Assert.Equal(5, room.FindPlayer("Some Player")!.Slot);

        room.Apply("Some Player changed to Red");
        Assert.Equal(TeamColour.Red, room.FindPlayer("Some Player")!.Team);

        room.Apply("Some Player left the game.");
        Assert.Empty(room.Players);
    }

    [Fact]
    public void Matches_players_across_the_space_underscore_spelling()
    {
        var room = new MultiplayerRoom(1);
        room.Apply("Some Player joined in slot 1.");

        Assert.NotNull(room.FindPlayer("Some_Player"));
    }

    [Fact]
    public void Clears_scores_when_a_new_map_starts()
    {
        var room = new MultiplayerRoom(1);

        room.Apply("Alpha joined in slot 1 for team red.");
        room.Apply("Alpha finished playing (Score: 500000, PASSED).");
        Assert.Equal(500000, room.FindPlayer("Alpha")!.LastScore);

        room.Apply("The match has started!");
        Assert.Null(room.FindPlayer("Alpha")!.LastScore);
        Assert.Equal(RoomState.Playing, room.State);

        room.Apply("The match has finished!");
        Assert.Equal(RoomState.Finished, room.State);
    }

    [Fact]
    public void Sums_scores_per_team()
    {
        var room = new MultiplayerRoom(1);

        room.Apply("Alpha joined in slot 1 for team red.");
        room.Apply("Beta joined in slot 2 for team red.");
        room.Apply("Gamma joined in slot 3 for team blue.");

        room.Apply("Alpha finished playing (Score: 300000, PASSED).");
        room.Apply("Beta finished playing (Score: 200000, PASSED).");
        room.Apply("Gamma finished playing (Score: 450000, PASSED).");

        Assert.Equal(500000, room.TeamScore(TeamColour.Red));
        Assert.Equal(450000, room.TeamScore(TeamColour.Blue));
    }

    [Fact]
    public void Tracks_the_current_beatmap_and_mods()
    {
        var room = new MultiplayerRoom(1);

        room.Apply("Changed beatmap to https://osu.ppy.sh/b/77 Artist - Title [Hard]");
        room.Apply("Active mods: HD, HR");

        Assert.Equal(77, room.CurrentBeatmapId);
        Assert.Equal("Artist - Title [Hard]", room.CurrentBeatmapName);
        Assert.Equal(Mods.Hidden | Mods.HardRock, room.CurrentMods);
    }

    [Fact]
    public void Ignores_lines_it_does_not_recognise()
    {
        var room = new MultiplayerRoom(1);

        Assert.Null(room.Apply("good luck everyone"));
        Assert.Empty(room.Players);
    }
}
