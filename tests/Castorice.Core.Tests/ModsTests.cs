using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class ModsTests
{
    [Theory]
    [InlineData("HDHR", Mods.Hidden | Mods.HardRock)]
    [InlineData("HD HR", Mods.Hidden | Mods.HardRock)]
    [InlineData("hd,hr", Mods.Hidden | Mods.HardRock)]
    [InlineData("DT", Mods.DoubleTime)]
    [InlineData("NM", Mods.None)]
    [InlineData("None", Mods.None)]
    [InlineData("", Mods.None)]
    [InlineData("FM", Mods.FreeMod)]
    [InlineData("Freemod", Mods.FreeMod)]
    [InlineData("HR+FM", Mods.HardRock | Mods.FreeMod)]
    public void Parses_mod_strings(string input, Mods expected) =>
        Assert.Equal(expected, ModsExtensions.ParseMods(input));

    [Fact]
    public void Ignores_unknown_acronyms()
    {
        Assert.Equal(Mods.Hidden, ModsExtensions.ParseMods("HD XY"));
    }

    [Theory]
    [InlineData("Hidden, HardRock", Mods.Hidden | Mods.HardRock)]
    [InlineData("DoubleTime", Mods.DoubleTime)]
    [InlineData("Team Red / HardRock", Mods.HardRock)]
    public void Parses_the_full_names_BanchoBot_prints(string input, Mods expected) =>
        Assert.Equal(expected, ModsExtensions.ParseMods(input));

    [Fact]
    public void Renders_acronyms_in_a_stable_order()
    {
        Assert.Equal("HD HR", (Mods.HardRock | Mods.Hidden).ToAcronyms());
        Assert.Equal("HDHR", (Mods.HardRock | Mods.Hidden).ToCompactAcronyms());
    }

    [Fact]
    public void Renders_the_no_mod_badge_as_NM()
    {
        Assert.Equal("NM", Mods.None.ToCompactAcronyms());
        Assert.Equal("None", Mods.None.ToAcronyms());
    }

    [Fact]
    public void Marks_freemod_on_top_of_forced_mods()
    {
        Assert.Equal("FM", Mods.FreeMod.ToCompactAcronyms());
        Assert.Equal("HR+FM", (Mods.HardRock | Mods.FreeMod).ToCompactAcronyms());
    }

    [Fact]
    public void Lists_acronyms_separately_for_a_set_of_alternatives()
    {
        // A set of choices must not render as one combination: "HD, EZ", never "EZHD".
        Assert.Equal(["EZ", "HD"], (Mods.Hidden | Mods.Easy).ToAcronymList());
        Assert.Equal(["HR"], Mods.HardRock.ToAcronymList());
        Assert.Empty(Mods.None.ToAcronymList());
    }

    [Fact]
    public void Round_trips_through_the_compact_form()
    {
        var mods = Mods.Hidden | Mods.DoubleTime;
        Assert.Equal(mods, ModsExtensions.ParseMods(mods.ToCompactAcronyms()));
    }
}
