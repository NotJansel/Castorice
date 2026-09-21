using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class MatchScoringTests
{
    private static PlayerScoreInput Red(string name, long score, Mods mods = Mods.None, bool passed = true) =>
        new(name, TeamColour.Red, score, passed, mods);

    private static PlayerScoreInput Blue(string name, long score, Mods mods = Mods.None, bool passed = true) =>
        new(name, TeamColour.Blue, score, passed, mods);

    [Fact]
    public void Totals_each_team()
    {
        var result = MatchScoring.Score(
            [Red("A", 300_000), Red("B", 200_000), Blue("C", 450_000)],
            Mods.None);

        Assert.Equal(500_000, result.RedTotal);
        Assert.Equal(450_000, result.BlueTotal);
        Assert.Equal(TeamColour.Red, result.Winner);
        Assert.Equal(50_000, result.Margin);
    }

    [Fact]
    public void A_failed_score_counts_as_nothing()
    {
        var result = MatchScoring.Score([Red("A", 900_000, passed: false), Blue("B", 100_000)], Mods.None);

        Assert.Equal(0, result.RedTotal);
        Assert.Equal(TeamColour.Blue, result.Winner);
    }

    [Fact]
    public void Multipliers_only_apply_on_a_freemod_pick()
    {
        var players = new[] { Red("A", 400_000, Mods.Easy), Blue("B", 500_000) };
        var multipliers = new ScoreMultipliers(Easy: 1.75, EasyHidden: 1.75);

        var forced = MatchScoring.Score(players, Mods.HardRock, multipliers);
        Assert.Equal(400_000, forced.RedTotal);
        Assert.Equal(TeamColour.Blue, forced.Winner);

        var free = MatchScoring.Score(players, Mods.FreeMod, multipliers);
        Assert.Equal(700_000, free.RedTotal);
        Assert.Equal(TeamColour.Red, free.Winner);
    }

    [Fact]
    public void Easy_with_hidden_uses_its_own_multiplier()
    {
        var multipliers = new ScoreMultipliers(Easy: 1.75, EasyHidden: 1.60);

        var result = MatchScoring.Score(
            [Red("A", 100_000, Mods.Easy), Red("B", 100_000, Mods.Easy | Mods.Hidden), Red("C", 100_000, Mods.Hidden)],
            Mods.FreeMod,
            multipliers);

        Assert.Equal(175_000 + 160_000 + 100_000, result.RedTotal);
    }

    [Fact]
    public void Records_what_was_adjusted_so_the_lobby_can_be_told()
    {
        var result = MatchScoring.Score(
            [Red("A", 100_000, Mods.Easy), Blue("B", 100_000, Mods.HardRock)],
            Mods.FreeMod,
            new ScoreMultipliers(1.75, 1.75));

        var adjusted = Assert.Single(result.Adjusted);
        Assert.Equal("A", adjusted.Username);
        Assert.Equal(175_000, adjusted.AdjustedScore);
    }

    [Fact]
    public void Reports_when_no_scores_arrived()
    {
        var result = MatchScoring.Score([Red("A", 0), Blue("B", 0)], Mods.None);

        Assert.False(result.HasScores);
        Assert.Null(result.Winner);
    }

    [Fact]
    public void A_draw_has_no_winner()
    {
        var result = MatchScoring.Score([Red("A", 500_000), Blue("B", 500_000)], Mods.None);

        Assert.True(result.HasScores);
        Assert.Null(result.Winner);
    }

    [Fact]
    public void A_lobby_without_teams_has_no_winner_to_award()
    {
        var result = MatchScoring.Score(
            [new PlayerScoreInput("A", null, 500_000, true, Mods.None)],
            Mods.None);

        Assert.False(result.HasTeams);
        Assert.Null(result.Winner);
    }

    [Fact]
    public void A_pick_without_overrides_follows_the_pool_default()
    {
        var pool = new Mappool { EasyMultiplier = 1.75, EasyHiddenMultiplier = 1.6 };
        var slot = new MappoolSlot { Label = "FM1", Mods = Mods.FreeMod };

        var resolved = pool.MultipliersFor(slot);

        Assert.Equal(1.75, resolved.Easy);
        Assert.Equal(1.6, resolved.EasyHidden);
        Assert.False(slot.HasMultiplierOverride);
    }

    [Fact]
    public void A_pick_can_carry_its_own_multipliers()
    {
        var pool = new Mappool { EasyMultiplier = 1.75, EasyHiddenMultiplier = 1.75 };
        var slot = new MappoolSlot { Label = "FM2", Mods = Mods.FreeMod, EasyMultiplier = 1.9 };

        var resolved = pool.MultipliersFor(slot);

        Assert.True(slot.HasMultiplierOverride);
        Assert.Equal(1.9, resolved.Easy);

        // Only the value that was overridden changes; the other still comes from the pool.
        Assert.Equal(1.75, resolved.EasyHidden);
    }

    [Fact]
    public void Two_freemod_picks_can_be_scored_differently()
    {
        var pool = new Mappool { EasyMultiplier = 1.75, EasyHiddenMultiplier = 1.75 };
        var lenient = new MappoolSlot { Label = "FM1", Mods = Mods.FreeMod };
        var strict = new MappoolSlot { Label = "FM2", Mods = Mods.FreeMod, EasyMultiplier = 1.2 };

        var players = new[] { Red("A", 100_000, Mods.Easy), Blue("B", 150_000) };

        Assert.Equal(175_000, MatchScoring.Score(players, Mods.FreeMod, pool.MultipliersFor(lenient)).RedTotal);
        Assert.Equal(120_000, MatchScoring.Score(players, Mods.FreeMod, pool.MultipliersFor(strict)).RedTotal);
    }

    [Fact]
    public void Clearing_an_override_returns_the_pick_to_the_pool_default()
    {
        var pool = new Mappool { EasyMultiplier = 1.75, EasyHiddenMultiplier = 1.75 };
        var slot = new MappoolSlot { Mods = Mods.FreeMod, EasyMultiplier = 1.2, EasyHiddenMultiplier = 1.1 };

        slot.ClearMultiplierOverride();

        Assert.False(slot.HasMultiplierOverride);
        Assert.Equal(1.75, pool.MultipliersFor(slot).Easy);
    }

    [Fact]
    public void Scoring_falls_back_to_the_pool_when_no_pick_is_known()
    {
        var pool = new Mappool { EasyMultiplier = 1.4, EasyHiddenMultiplier = 1.3 };

        Assert.Equal(1.4, pool.MultipliersFor(null).Easy);
    }

    [Fact]
    public void Only_freemod_picks_are_offered_for_a_multiplier()
    {
        var pool = new Mappool
        {
            Slots =
            [
                new MappoolSlot { Label = "NM1", Mods = Mods.None },
                new MappoolSlot { Label = "FM1", Mods = Mods.FreeMod },
                new MappoolSlot { Label = "TB", Mods = Mods.FreeMod },
            ],
        };

        Assert.Equal(["FM1", "TB"], pool.FreeModSlots.Select(s => s.Label));
    }

    [Theory]
    [InlineData(SlotAvailability.BannedByRed, true, false)]
    [InlineData(SlotAvailability.BannedByBlue, true, false)]
    [InlineData(SlotAvailability.ProtectedByRed, false, true)]
    [InlineData(SlotAvailability.ProtectedByBlue, false, true)]
    [InlineData(SlotAvailability.Available, false, false)]
    public void Classifies_bans_and_protects(SlotAvailability availability, bool banned, bool prot)
    {
        Assert.Equal(banned, availability.IsBanned());
        Assert.Equal(prot, availability.IsProtected());
    }

    [Fact]
    public void Attributes_a_ban_or_protect_to_its_team()
    {
        Assert.Equal(TeamColour.Red, SlotAvailability.BannedByRed.Team());
        Assert.Equal(TeamColour.Blue, SlotAvailability.ProtectedByBlue.Team());
        Assert.Null(SlotAvailability.Available.Team());
    }
}
