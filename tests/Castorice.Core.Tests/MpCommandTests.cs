using Castorice.Core.Bancho;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class MpCommandTests
{
    [Fact]
    public void Map_includes_the_play_mode()
    {
        Assert.Equal("!mp map 1234567 0", MpCommands.Map(1234567));
        Assert.Equal("!mp map 1234567 3", MpCommands.Map(1234567, PlayMode.Mania));
    }

    [Fact]
    public void Map_rejects_a_missing_beatmap_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MpCommands.Map(0));
    }

    [Fact]
    public void Mods_are_sent_as_spaced_acronyms()
    {
        Assert.Equal("!mp mods HD HR", MpCommands.SetMods(Mods.Hidden | Mods.HardRock));
        Assert.Equal("!mp mods None", MpCommands.SetMods(Mods.None));
        Assert.Equal("!mp mods HR Freemod", MpCommands.SetMods(Mods.HardRock | Mods.FreeMod));
    }

    [Fact]
    public void Set_omits_the_size_when_it_is_not_given()
    {
        Assert.Equal("!mp set 2 3", MpCommands.Set(TeamMode.TeamVs, ScoreMode.ScoreV2));
        Assert.Equal("!mp set 2 3 16", MpCommands.Set(TeamMode.TeamVs, ScoreMode.ScoreV2, 16));
    }

    [Fact]
    public void Usernames_with_spaces_become_underscored()
    {
        Assert.Equal("!mp invite Some_Player", MpCommands.Invite("Some Player"));
        Assert.Equal("!mp team Some_Player red", MpCommands.Team("Some Player", TeamColour.Red));
    }

    [Fact]
    public void Start_clamps_the_countdown()
    {
        Assert.Equal("!mp start", MpCommands.Start());
        Assert.Equal("!mp start 10", MpCommands.Start(10));
        Assert.Equal("!mp start 300", MpCommands.Start(9999));
    }

    [Fact]
    public void Newlines_cannot_be_smuggled_into_a_room_name()
    {
        var command = MpCommands.Make("Cup: (A) vs (B)\r\nQUIT");

        Assert.DoesNotContain('\r', command);
        Assert.DoesNotContain('\n', command);
    }

    [Fact]
    public void Picking_a_slot_sets_the_map_then_the_mods()
    {
        var slot = new MappoolSlot { Label = "HD1", BeatmapId = 42, Mods = Mods.Hidden };

        Assert.Equal(["!mp map 42 0", "!mp mods HD"], MpCommands.PickSlot(slot, PlayMode.Osu).ToArray());
    }

    [Fact]
    public void Configuring_a_room_sets_the_mode_and_adds_referees()
    {
        var pool = new Mappool
        {
            TeamMode = TeamMode.TeamVs,
            ScoreMode = ScoreMode.ScoreV2,
            RoomSize = 8,
            Referees = ["Ref One", "  ", "RefTwo"],
        };

        Assert.Equal(
            ["!mp set 2 3 8", "!mp addref Ref_One", "!mp addref RefTwo"],
            MpCommands.ConfigureRoom(pool).ToArray());
    }
}
