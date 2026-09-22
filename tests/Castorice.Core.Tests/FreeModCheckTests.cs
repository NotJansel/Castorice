using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class FreeModCheckTests
{
    private static PlayerScoreInput Player(string name, Mods mods) =>
        new(name, TeamColour.Red, 0, true, mods);

    [Fact]
    public void Passes_a_lobby_where_everyone_took_an_allowed_mod()
    {
        var result = FreeModCheck.Check(
            [Player("A", Mods.Hidden), Player("B", Mods.HardRock), Player("C", Mods.Easy | Mods.Hidden)]);

        Assert.True(result.IsClean);
        Assert.Equal(3, result.PlayersChecked);
        Assert.Equal("FreeMod check: everyone is good.", result.Summary);
    }

    [Fact]
    public void Names_a_player_who_took_no_mod()
    {
        var result = FreeModCheck.Check([Player("A", Mods.Hidden), Player("NoMods", Mods.None)]);

        var violation = Assert.Single(result.Violations);
        Assert.Equal("NoMods", violation.Username);
        Assert.Equal(FreeModProblem.NoRequiredMod, violation.Problem);
        Assert.Contains("NoMods: no mod", result.Summary);
    }

    [Fact]
    public void NoFail_alone_does_not_satisfy_the_requirement()
    {
        // NoFail is a safety net, not a difficulty choice, so it neither counts nor offends.
        var result = FreeModCheck.Check([Player("A", Mods.NoFail)]);

        Assert.Equal(FreeModProblem.NoRequiredMod, Assert.Single(result.Violations).Problem);
    }

    [Fact]
    public void NoFail_on_top_of_an_allowed_mod_is_fine()
    {
        Assert.True(FreeModCheck.Check([Player("A", Mods.NoFail | Mods.Hidden)]).IsClean);
    }

    [Fact]
    public void Flags_a_mod_the_bracket_does_not_permit()
    {
        var result = FreeModCheck.Check([Player("Cheeky", Mods.DoubleTime)]);

        var violation = Assert.Single(result.Violations);
        Assert.Equal(FreeModProblem.ForbiddenMod, violation.Problem);
        Assert.Contains("Cheeky: DT not allowed", result.Summary);
    }

    [Fact]
    public void Reports_only_the_offending_part_of_a_mod_combination()
    {
        var result = FreeModCheck.Check([Player("A", Mods.Hidden | Mods.DoubleTime)]);

        Assert.Equal(Mods.DoubleTime, Assert.Single(result.Violations).Mods);
    }

    [Fact]
    public void Honours_a_brackets_own_allowed_set()
    {
        var players = new[] { Player("A", Mods.DoubleTime) };

        Assert.False(FreeModCheck.Check(players).IsClean);
        Assert.True(FreeModCheck.Check(players, allowed: Mods.DoubleTime | Mods.Hidden).IsClean);
    }

    [Fact]
    public void Can_be_told_not_to_require_a_mod_at_all()
    {
        Assert.True(FreeModCheck.Check([Player("A", Mods.None)], requireAtLeastOne: false).IsClean);
    }

    [Fact]
    public void Says_when_it_had_nothing_to_look_at()
    {
        var result = FreeModCheck.Check([]);

        Assert.False(result.HasData);
        Assert.True(result.IsClean);
    }

    [Fact]
    public void The_freemod_flag_itself_is_not_a_player_mod()
    {
        // Bancho reports the room's FreeMod flag alongside a player's own mods.
        Assert.True(FreeModCheck.Check([Player("A", Mods.FreeMod | Mods.HardRock)]).IsClean);
    }

    [Fact]
    public void A_pool_carries_its_own_freemod_rule()
    {
        var pool = new Mappool { FreeModAllowedMods = Mods.Hidden, FreeModRequiresAMod = false };

        Assert.Equal(Mods.Hidden, pool.FreeModAllowedMods);
        Assert.False(pool.FreeModRequiresAMod);
        Assert.Equal(FreeModCheck.DefaultAllowed, new Mappool().FreeModAllowedMods);
    }
}
