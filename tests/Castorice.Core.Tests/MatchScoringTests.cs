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
