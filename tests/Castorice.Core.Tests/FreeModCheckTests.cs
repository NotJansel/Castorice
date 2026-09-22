using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class FreeModCheckTests
{
    private static PlayerScoreInput P(string name, TeamColour? team, Mods mods) =>
        new(name, team, 0, true, mods);

    private static PlayerScoreInput Red(string name, Mods mods) => P(name, TeamColour.Red, mods);

    private static PlayerScoreInput Blue(string name, Mods mods) => P(name, TeamColour.Blue, mods);

    /// <summary>A legal 3v3 side: one HardRock, one Hidden, one free to play NoMod.</summary>
    private static PlayerScoreInput[] LegalThree(Func<string, Mods, PlayerScoreInput> team, string prefix) =>
    [
        team($"{prefix}1", Mods.HardRock),
        team($"{prefix}2", Mods.Hidden),
        team($"{prefix}3", Mods.None),
    ];

    [Fact]
    public void A_three_versus_three_may_field_one_nomod_player_per_team()
    {
        var result = FreeModCheck.Check([.. LegalThree(Red, "R"), .. LegalThree(Blue, "B")]);

        Assert.True(result.IsClean);
        Assert.Equal(6, result.PlayersChecked);
    }

    [Fact]
    public void A_four_versus_four_may_field_two_nomod_players_per_team()
    {
        var result = FreeModCheck.Check(
        [
            Red("R1", Mods.HardRock), Red("R2", Mods.Hidden), Red("R3", Mods.None), Red("R4", Mods.None),
            Blue("B1", Mods.HardRock), Blue("B2", Mods.Easy), Blue("B3", Mods.None), Blue("B4", Mods.None),
        ]);

        Assert.True(result.IsClean);
    }

    [Fact]
    public void A_team_without_a_hardrock_player_is_short()
    {
        var result = FreeModCheck.Check(
        [
            Red("R1", Mods.Hidden), Red("R2", Mods.Easy), Red("R3", Mods.None),
            .. LegalThree(Blue, "B"),
        ]);

        var missing = Assert.Single(result.MissingGroups);
        Assert.Equal(TeamColour.Red, missing.Team);
        Assert.Equal("HR", missing.GroupName);
        Assert.Equal(1, missing.Shortfall);
        Assert.Contains("Red needs 1x HR", result.Summary);
    }

    [Fact]
    public void A_team_without_a_hidden_or_easy_player_is_short()
    {
        var result = FreeModCheck.Check(
        [
            Red("R1", Mods.HardRock), Red("R2", Mods.None), Red("R3", Mods.None),
            .. LegalThree(Blue, "B"),
        ]);

        Assert.Contains("Red needs 1x HD/EZ", result.Summary);
    }

    [Fact]
    public void Easy_and_easy_hidden_both_fill_the_hidden_slot()
    {
        foreach (var mods in new[] { Mods.Easy, Mods.Easy | Mods.Hidden })
        {
            var result = FreeModCheck.Check([Red("R1", Mods.HardRock), Red("R2", mods), Red("R3", Mods.None)]);

            Assert.True(result.IsClean, $"{mods.ToCompactAcronyms()} should fill the HD/EZ slot");
        }
    }

    [Fact]
    public void An_hdhr_player_fills_the_hardrock_slot_not_the_hidden_one()
    {
        // Ordering matters: HDHR carries Hidden too, but it is a HardRock player.
        var result = FreeModCheck.Check(
            [Red("R1", Mods.Hidden | Mods.HardRock), Red("R2", Mods.Hidden), Red("R3", Mods.None)]);

        Assert.True(result.IsClean);

        // With the Hidden player swapped for a NoMod one, the Hidden slot is now unfilled.
        var short_ = FreeModCheck.Check(
            [Red("R1", Mods.Hidden | Mods.HardRock), Red("R2", Mods.None), Red("R3", Mods.None)]);

        Assert.Contains("Red needs 1x HD/EZ", short_.Summary);
    }

    [Fact]
    public void Both_teams_are_reported_separately()
    {
        var result = FreeModCheck.Check(
        [
            Red("R1", Mods.None), Red("R2", Mods.None), Red("R3", Mods.None),
            Blue("B1", Mods.None), Blue("B2", Mods.None), Blue("B3", Mods.None),
        ]);

        Assert.Equal(4, result.MissingGroups.Count);
        Assert.Contains("Red needs 1x HR", result.Summary);
        Assert.Contains("Blue needs 1x HR", result.Summary);
    }

    [Fact]
    public void A_side_that_is_not_in_the_lobby_is_not_a_violation()
    {
        var result = FreeModCheck.Check([.. LegalThree(Red, "R")]);

        Assert.True(result.IsClean);
    }

    [Fact]
    public void NoFail_neither_fills_a_slot_nor_offends()
    {
        Assert.True(FreeModCheck.Check(
            [Red("R1", Mods.HardRock | Mods.NoFail), Red("R2", Mods.Hidden), Red("R3", Mods.NoFail)]).IsClean);

        Assert.Contains("Red needs 1x HR", FreeModCheck.Check(
            [Red("R1", Mods.NoFail), Red("R2", Mods.Hidden), Red("R3", Mods.None)]).Summary);
    }

    [Fact]
    public void The_rooms_freemod_flag_is_not_a_player_mod()
    {
        Assert.True(FreeModCheck.Check(
        [
            Red("R1", Mods.FreeMod | Mods.HardRock),
            Red("R2", Mods.FreeMod | Mods.Hidden),
            Red("R3", Mods.FreeMod),
        ]).IsClean);
    }

    [Fact]
    public void Flags_a_mod_the_bracket_does_not_permit()
    {
        var result = FreeModCheck.Check([.. LegalThree(Red, "R"), Red("Cheeky", Mods.DoubleTime)]);

        var forbidden = Assert.Single(result.ForbiddenMods);
        Assert.Equal("Cheeky", forbidden.Username);
        Assert.Contains("Cheeky: DT not allowed", result.Summary);
    }

    [Fact]
    public void Reports_only_the_offending_part_of_a_combination()
    {
        var result = FreeModCheck.Check([Red("A", Mods.Hidden | Mods.DoubleTime)]);

        Assert.Equal(Mods.DoubleTime, Assert.Single(result.ForbiddenMods).Mods);
    }

    [Fact]
    public void A_bracket_can_demand_two_of_a_group()
    {
        List<FreeModGroup> strict =
        [
            new() { Name = "HR", AnyOf = Mods.HardRock, MinimumPerTeam = 2 },
            new() { Name = "HD/EZ", AnyOf = Mods.Hidden | Mods.Easy, MinimumPerTeam = 1 },
        ];

        var result = FreeModCheck.Check([.. LegalThree(Red, "R")], strict);

        Assert.Equal("FreeMod check: Red needs 1x HR", result.Summary);
    }

    [Fact]
    public void Says_when_it_had_nothing_to_look_at()
    {
        var result = FreeModCheck.Check([]);

        Assert.False(result.HasData);
        Assert.True(result.IsClean);
    }

    [Fact]
    public void A_pool_carries_the_default_quota()
    {
        var pool = new Mappool();

        Assert.Equal(["HR", "HD/EZ"], pool.FreeModGroups.Select(g => g.Name));
        Assert.Equal(FreeModCheck.DefaultAllowed, pool.FreeModAllowedMods);
    }
}
