using System.Text.Json;
using Castorice.Core.Bancho;
using Castorice.Core.Configuration;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

/// <summary>
/// Guards the sample pool that ships with the repository: it is the first file a new user copies,
/// so it has to deserialise with exactly the options the app uses.
/// </summary>
public class SampleMappoolTests
{
    private static Mappool LoadSample()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "example-mappool.json");
        Assert.True(File.Exists(path), $"The sample mappool was not copied to {path}.");

        var pool = JsonSerializer.Deserialize<Mappool>(File.ReadAllText(path), CastoriceJson.Options);
        Assert.NotNull(pool);
        return pool;
    }

    [Fact]
    public void Deserialises_with_the_application_options()
    {
        var pool = LoadSample();

        Assert.Equal("CC", pool.Acronym);
        Assert.Equal(TeamMode.TeamVs, pool.TeamMode);
        Assert.Equal(ScoreMode.ScoreV2, pool.ScoreMode);
        Assert.Equal(PlayMode.Osu, pool.PlayMode);
        Assert.Equal(11, pool.Slots.Count);
    }

    [Fact]
    public void Parses_every_slots_mods()
    {
        var pool = LoadSample();

        Assert.Equal(Mods.None, pool.Slots.Single(s => s.Label == "NM1").Mods);
        Assert.Equal(Mods.Hidden, pool.Slots.Single(s => s.Label == "HD1").Mods);
        Assert.Equal(Mods.HardRock, pool.Slots.Single(s => s.Label == "HR1").Mods);
        Assert.Equal(Mods.DoubleTime, pool.Slots.Single(s => s.Label == "DT1").Mods);
        Assert.Equal(Mods.FreeMod, pool.Slots.Single(s => s.Label == "TB").Mods);
    }

    [Fact]
    public void Every_slot_produces_a_usable_pick_command()
    {
        foreach (var slot in LoadSample().Slots)
        {
            var commands = MpCommands.PickSlot(slot, PlayMode.Osu).ToArray();

            Assert.Equal(2, commands.Length);
            Assert.StartsWith("!mp map ", commands[0]);
            Assert.StartsWith("!mp mods ", commands[1]);
        }
    }
}
