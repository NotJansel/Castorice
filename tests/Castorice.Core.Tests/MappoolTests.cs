using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class MappoolTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "castorice-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Round_trips_a_pool_through_disk()
    {
        var store = new MappoolStore(_directory);
        var pool = new Mappool
        {
            Name = "OWC Finals",
            Acronym = "OWC",
            Stage = "Finals",
            TeamMode = TeamMode.TeamVs,
            ScoreMode = ScoreMode.ScoreV2,
            Referees = ["Ref One"],
            Slots =
            [
                new MappoolSlot { Label = "NM1", BeatmapId = 100, Mods = Mods.None, Title = "A" },
                new MappoolSlot { Label = "HD1", BeatmapId = 200, Mods = Mods.Hidden, Title = "B" },
                new MappoolSlot { Label = "TB", BeatmapId = 300, Mods = Mods.FreeMod, Title = "C" },
            ],
        };

        var fileName = store.Save(pool);
        var loaded = store.Load(fileName);

        Assert.NotNull(loaded);
        Assert.Equal("OWC Finals", loaded.Name);
        Assert.Equal(3, loaded.Slots.Count);
        Assert.Equal(Mods.Hidden, loaded.Slots[1].Mods);
        Assert.Equal(Mods.FreeMod, loaded.Slots[2].Mods);
        Assert.Equal(["Ref One"], loaded.Referees);
    }

    [Fact]
    public void Lists_pools_by_their_display_name()
    {
        var store = new MappoolStore(_directory);
        store.Save(new Mappool { Name = "Zeta Cup" });
        store.Save(new Mappool { Name = "Alpha Cup" });

        var listed = store.List();

        Assert.Equal(2, listed.Count);
        Assert.Equal("Alpha Cup", listed[0].DisplayName);
    }

    [Fact]
    public void Returns_null_for_a_missing_pool()
    {
        Assert.Null(new MappoolStore(_directory).Load("nope.json"));
    }

    [Theory]
    [InlineData("OWC Finals", "owc-finals.json")]
    [InlineData("A / B: C", "a-b-c.json")]
    [InlineData("   ", "mappool.json")]
    public void Suggests_a_portable_file_name(string poolName, string expected) =>
        Assert.Equal(expected, MappoolStore.SuggestFileName(poolName));

    [Fact]
    public void Derives_the_category_from_the_label_when_none_is_set()
    {
        Assert.Equal("HD", new MappoolSlot { Label = "HD2" }.EffectiveCategory);
        Assert.Equal("Hidden", new MappoolSlot { Label = "HD2", Category = "Hidden" }.EffectiveCategory);
        Assert.Equal("MISC", new MappoolSlot { Label = "1" }.EffectiveCategory);
    }

    [Fact]
    public void Builds_a_room_name_from_the_pool_and_teams()
    {
        var pool = new Mappool { Name = "Cup", Acronym = "OWC", Stage = "Quarterfinals" };

        Assert.Equal("OWC: Quarterfinals: (Red Team) vs (Blue Team)", pool.BuildRoomName("Red Team", "Blue Team"));
        Assert.Equal("OWC: Quarterfinals: (Red) vs (Blue)", pool.BuildRoomName("", "  "));
    }

    [Fact]
    public void Falls_back_to_the_pool_name_without_an_acronym()
    {
        var pool = new Mappool { Name = "Weekly Cup", Acronym = "", Stage = "" };

        Assert.Equal("Weekly Cup: (A) vs (B)", pool.BuildRoomName("A", "B"));
    }
}
