using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class MatchAnnouncerTests
{
    private static MatchStanding Standing(int red = 1, int blue = 0, int toWin = 7) =>
        new(red, blue, toWin, "Red", "Blue");

    private static MapResult TeamResult(long red, long blue, Mods redMods = Mods.None) =>
        MatchScoring.Score(
            [
                new PlayerScoreInput("A", TeamColour.Red, red, true, redMods),
                new PlayerScoreInput("B", TeamColour.Blue, blue, true, Mods.None),
            ],
            Mods.None);

    [Fact]
    public void Announces_the_map_result_and_the_running_score()
    {
        var messages = MatchAnnouncer.BuildResultMessages(
            TeamResult(1_234_567, 1_000_000),
            "NM1",
            "Artist - Title [Expert]",
            Standing(red: 3, blue: 2));

        Assert.Equal(2, messages.Count);
        Assert.Equal(
            "[NM1] Artist - Title [Expert] | Red 1,234,567 - 1,000,000 Blue | Red wins by 234,567",
            messages[0]);
        Assert.Equal("Match score: Red 3 - 2 Blue (first to 7)", messages[1]);
    }

    [Fact]
    public void Calls_the_match_once_a_team_reaches_the_target()
    {
        var messages = MatchAnnouncer.BuildResultMessages(
            TeamResult(900_000, 100_000),
            "TB",
            "Artist - Title [Finale]",
            Standing(red: 7, blue: 5));

        Assert.Equal("Red wins the match 7 - 5", messages[^1]);
    }

    [Fact]
    public void Reports_a_tie_without_claiming_a_winner()
    {
        var messages = MatchAnnouncer.BuildResultMessages(
            TeamResult(500_000, 500_000),
            "NM2",
            "Artist - Title [Hard]",
            Standing(red: 1, blue: 1));

        Assert.Contains("| tied", messages[0]);
    }

    [Fact]
    public void Spells_out_every_multiplier_it_applied()
    {
        var result = MatchScoring.Score(
            [
                new PlayerScoreInput("EasyPlayer", TeamColour.Red, 100_000, true, Mods.Easy),
                new PlayerScoreInput("EzhdPlayer", TeamColour.Red, 100_000, true, Mods.Easy | Mods.Hidden),
                new PlayerScoreInput("Plain", TeamColour.Blue, 300_000, true, Mods.None),
            ],
            Mods.FreeMod,
            new ScoreMultipliers(1.75, 1.6));

        var messages = MatchAnnouncer.BuildResultMessages(result, "FM1", "Artist - Title [Free]", Standing());

        var line = Assert.Single(messages, m => m.StartsWith("Multipliers:", StringComparison.Ordinal));
        Assert.Contains("EasyPlayer EZ x1.75 (100,000 -> 175,000)", line);
        Assert.Contains("EzhdPlayer EZHD x1.6 (100,000 -> 160,000)", line);
        Assert.DoesNotContain("Plain", line);
    }

    [Fact]
    public void Says_nothing_at_all_when_no_scores_were_captured()
    {
        var result = MatchScoring.Score(
            [new PlayerScoreInput("A", TeamColour.Red, 0, true, Mods.None)],
            Mods.None);

        Assert.Empty(MatchAnnouncer.BuildResultMessages(result, "NM1", "Map", Standing()));
    }

    [Fact]
    public void Ranks_players_when_the_lobby_is_not_playing_in_teams()
    {
        var result = MatchScoring.Score(
            [
                new PlayerScoreInput("A", null, 300_000, true, Mods.None),
                new PlayerScoreInput("B", null, 500_000, true, Mods.None),
            ],
            Mods.None);

        var messages = MatchAnnouncer.BuildResultMessages(result, "NM1", "Map", Standing());

        // No teams means no point to award, so only the ranking line goes out.
        var single = Assert.Single(messages);
        Assert.Contains("1. B 500,000", single);
        Assert.Contains("2. A 300,000", single);
    }

    [Fact]
    public void Uses_the_team_names_it_was_given()
    {
        var messages = MatchAnnouncer.BuildResultMessages(
            TeamResult(600_000, 100_000),
            "NM1",
            "Map",
            new MatchStanding(1, 0, 7, "Gravity", "Phantom"));

        Assert.Contains("Gravity 600,000 - 100,000 Phantom", messages[0]);
        Assert.Contains("Gravity wins by 500,000", messages[0]);
    }
}
