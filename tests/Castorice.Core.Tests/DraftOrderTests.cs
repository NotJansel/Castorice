using System.Text.Json;
using Castorice.Core.Configuration;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class DraftOrderTests
{
    private const TeamColour R = TeamColour.Red;
    private const TeamColour B = TeamColour.Blue;

    private static DraftRules Rules(
        int protects = 0,
        int bans = 2,
        TurnOrder banOrder = TurnOrder.Alternating,
        PickOrder pickOrder = PickOrder.Alternating) => new()
    {
        ProtectsPerTeam = protects,
        BansPerTeam = bans,
        BanOrder = banOrder,
        PickOrder = pickOrder,
    };

    private static DraftTurn Next(DraftRules rules, DraftProgress progress, DraftStart? start = null) =>
        DraftOrder.Evaluate(rules, start ?? DraftStart.Red, progress).Next;

    /// <summary>Marks the given sequence one at a time and records whose turn the draft said it was.</summary>
    private static List<TeamColour?> BanTurns(DraftRules rules, DraftStart start)
    {
        var turns = new List<TeamColour?>();
        var red = 0;
        var blue = 0;

        for (var i = 0; i < rules.BansPerTeam * 2; i++)
        {
            var next = DraftOrder.Evaluate(rules, start, new DraftProgress { RedBans = red, BlueBans = blue, PointsToWin = 7 }).Next;
            Assert.Equal(DraftPhase.Ban, next.Phase);
            turns.Add(next.Team);

            if (next.Team is R)
            {
                red++;
            }
            else
            {
                blue++;
            }
        }

        return turns;
    }

    [Fact]
    public void Alternating_bans_go_A_B_A_B()
    {
        Assert.Equal([R, B, R, B], BanTurns(Rules(bans: 2), DraftStart.Red));
    }

    [Fact]
    public void Snake_bans_go_A_B_B_A()
    {
        Assert.Equal([R, B, B, R], BanTurns(Rules(bans: 2, banOrder: TurnOrder.Snake), DraftStart.Red));
    }

    [Fact]
    public void Snake_keeps_its_shape_over_longer_rounds()
    {
        Assert.Equal(
            [B, R, R, B, B, R],
            BanTurns(Rules(bans: 3, banOrder: TurnOrder.Snake), new DraftStart(R, B, R)));
    }

    [Fact]
    public void The_first_ban_team_opens_the_round()
    {
        Assert.Equal([B, R, B, R], BanTurns(Rules(bans: 2), new DraftStart(R, B, R)));
    }

    [Fact]
    public void Protects_come_before_bans_and_have_their_own_order()
    {
        var rules = Rules(protects: 1, bans: 1);
        var start = new DraftStart(FirstProtect: B, FirstBan: R, FirstPick: R);

        var first = Next(rules, new DraftProgress { PointsToWin = 7 }, start);
        Assert.Equal(new DraftTurn(DraftPhase.Protect, B, 1, 2), first);

        var second = Next(rules, new DraftProgress { BlueProtects = 1, PointsToWin = 7 }, start);
        Assert.Equal(new DraftTurn(DraftPhase.Protect, R, 2, 2), second);

        var ban = Next(rules, new DraftProgress { RedProtects = 1, BlueProtects = 1, PointsToWin = 7 }, start);
        Assert.Equal(new DraftTurn(DraftPhase.Ban, R, 1, 2), ban);
    }

    [Fact]
    public void A_ban_marked_for_the_wrong_team_does_not_shift_the_order()
    {
        // ABAB with Red first: Red should ban, then Blue. The referee marked two for Red.
        var next = Next(Rules(bans: 2), new DraftProgress { RedBans = 2, PointsToWin = 7 });

        Assert.Equal(DraftPhase.Ban, next.Phase);
        Assert.Equal(B, next.Team);
        Assert.Equal(2, next.Number);
    }

    [Fact]
    public void Picks_start_once_every_ban_is_in()
    {
        var state = DraftOrder.Evaluate(
            Rules(bans: 1),
            new DraftStart(R, R, B),
            new DraftProgress { RedBans = 1, BlueBans = 1, PointsToWin = 7 });

        Assert.Equal(DraftPhase.Pick, state.Next.Phase);
        Assert.Equal(B, state.Next.Team);
        Assert.Equal(B, state.NextPicker);
    }

    [Fact]
    public void The_next_picker_is_known_even_while_bans_are_outstanding()
    {
        var state = DraftOrder.Evaluate(Rules(bans: 1), new DraftStart(R, R, B), new DraftProgress { PointsToWin = 7 });

        Assert.Equal(DraftPhase.Ban, state.Next.Phase);
        Assert.Equal(B, state.NextPicker);
    }

    [Theory]
    [InlineData(PickOrder.Alternating, new[] { R, B, R, B, R, B })]
    [InlineData(PickOrder.Snake, new[] { R, B, B, R, R, B })]
    public void Fixed_pick_orders_follow_their_pattern(PickOrder order, TeamColour[] expected)
    {
        var rules = Rules(bans: 0, pickOrder: order);
        var picks = new List<TeamColour?>();

        foreach (var want in expected)
        {
            var state = DraftOrder.Evaluate(rules, DraftStart.Red, new DraftProgress { Picks = picks, PointsToWin = 7 });
            Assert.Equal(want, state.NextPicker);
            picks.Add(state.NextPicker);
        }
    }

    [Fact]
    public void Loser_picks_hands_the_turn_to_whoever_lost_the_last_map()
    {
        var rules = Rules(bans: 0, pickOrder: PickOrder.LoserPicks);

        var opening = DraftOrder.Evaluate(rules, DraftStart.Red, new DraftProgress { PointsToWin = 7 });
        Assert.Equal(R, opening.NextPicker);

        var afterRedWon = DraftOrder.Evaluate(
            rules,
            DraftStart.Red,
            new DraftProgress { Picks = [R], MapWinners = [R], RedScore = 1, PointsToWin = 7 });
        Assert.Equal(B, afterRedWon.NextPicker);

        var afterBlueLost = DraftOrder.Evaluate(
            rules,
            DraftStart.Red,
            new DraftProgress { Picks = [R, B], MapWinners = [R, R], RedScore = 2, PointsToWin = 7 });
        Assert.Equal(B, afterBlueLost.NextPicker);
    }

    [Fact]
    public void Winner_picks_hands_the_turn_to_whoever_won_the_last_map()
    {
        var state = DraftOrder.Evaluate(
            Rules(bans: 0, pickOrder: PickOrder.WinnerPicks),
            DraftStart.Red,
            new DraftProgress { Picks = [R], MapWinners = [B], BlueScore = 1, PointsToWin = 7 });

        Assert.Equal(B, state.NextPicker);
    }

    [Fact]
    public void Loser_picks_waits_for_the_map_being_played()
    {
        var state = DraftOrder.Evaluate(
            Rules(bans: 0, pickOrder: PickOrder.LoserPicks),
            DraftStart.Red,
            new DraftProgress { Picks = [R], MapWinners = [], PointsToWin = 7 });

        Assert.Equal(DraftPhase.Pick, state.Next.Phase);
        Assert.Null(state.NextPicker);
        Assert.Null(MatchAnnouncer.NextTurnLine(state.Next, TeamNames.Default));
    }

    [Fact]
    public void A_tied_map_passes_the_turn_on_under_loser_picks()
    {
        var state = DraftOrder.Evaluate(
            Rules(bans: 0, pickOrder: PickOrder.LoserPicks),
            DraftStart.Red,
            new DraftProgress { Picks = [R], MapWinners = [null], PointsToWin = 7 });

        Assert.Equal(B, state.NextPicker);
    }

    [Fact]
    public void A_second_ban_round_follows_the_configured_number_of_picks()
    {
        var rules = Rules(bans: 1);
        rules.SecondBanRoundAfterPicks = 2;
        rules.SecondRoundBansPerTeam = 1;

        var beforeRound = Next(rules, new DraftProgress { RedBans = 1, BlueBans = 1, Picks = [R], PointsToWin = 7 });
        Assert.Equal(DraftPhase.Pick, beforeRound.Phase);

        var round = Next(rules, new DraftProgress { RedBans = 1, BlueBans = 1, Picks = [R, B], PointsToWin = 7 });
        Assert.Equal(new DraftTurn(DraftPhase.Ban, R, 1, 2, Round: 2), round);

        var after = Next(rules, new DraftProgress { RedBans = 2, BlueBans = 2, Picks = [R, B], PointsToWin = 7 });
        Assert.Equal(DraftPhase.Pick, after.Phase);
        Assert.Equal(R, after.Team);
    }

    [Fact]
    public void The_second_ban_round_can_open_with_the_other_team()
    {
        var rules = Rules(bans: 1);
        rules.SecondBanRoundAfterPicks = 2;
        rules.SecondRoundOtherTeamFirst = true;

        var round = Next(rules, new DraftProgress { RedBans = 1, BlueBans = 1, Picks = [R, B], PointsToWin = 7 });

        Assert.Equal(B, round.Team);
        Assert.Equal(2, round.Round);
    }

    [Fact]
    public void Calls_the_tiebreaker_when_both_teams_are_one_point_short()
    {
        var next = Next(Rules(bans: 0), new DraftProgress { Picks = [R, B], RedScore = 1, BlueScore = 1, PointsToWin = 2 });

        Assert.Equal(DraftPhase.Tiebreaker, next.Phase);
        Assert.Equal("Next: Tiebreaker", MatchAnnouncer.NextTurnLine(next, TeamNames.Default));
    }

    [Fact]
    public void A_best_of_one_never_calls_a_tiebreaker_at_nil_nil()
    {
        Assert.Equal(DraftPhase.Pick, Next(Rules(bans: 0), new DraftProgress { PointsToWin = 1 }).Phase);
    }

    [Fact]
    public void The_draft_finishes_with_the_match()
    {
        var next = Next(Rules(bans: 0), new DraftProgress { Picks = [R, B, R], RedScore = 2, BlueScore = 1, PointsToWin = 2 });

        Assert.Equal(DraftPhase.Finished, next.Phase);
        Assert.Null(MatchAnnouncer.NextTurnLine(next, TeamNames.Default));
    }

    [Fact]
    public void No_protects_and_no_bans_goes_straight_to_picks()
    {
        Assert.Equal(DraftPhase.Pick, Next(Rules(protects: 0, bans: 0), new DraftProgress { PointsToWin = 7 }).Phase);
    }

    [Fact]
    public void Describes_the_turn_for_the_referee()
    {
        var names = TeamNames.From("Germany", " ");

        Assert.Equal("Germany bans · 3 of 4", new DraftTurn(DraftPhase.Ban, R, 3, 4).Describe(names, PickOrder.Alternating));
        Assert.Equal("Blue protects · 1 of 2", new DraftTurn(DraftPhase.Protect, B, 1, 2).Describe(names, PickOrder.Alternating));
        Assert.Equal("The loser of this map picks next", new DraftTurn(DraftPhase.Pick, null).Describe(names, PickOrder.LoserPicks));
    }

    [Fact]
    public void Draft_rules_survive_the_pool_file()
    {
        var pool = new Mappool
        {
            Draft = new DraftRules
            {
                ProtectsPerTeam = 1,
                BansPerTeam = 2,
                BanOrder = TurnOrder.Snake,
                PickOrder = PickOrder.LoserPicks,
                SecondBanRoundAfterPicks = 4,
                SecondRoundOtherTeamFirst = true,
            },
        };

        var json = JsonSerializer.Serialize(pool, CastoriceJson.Options);
        var loaded = JsonSerializer.Deserialize<Mappool>(json, CastoriceJson.Options)!;

        Assert.Contains("\"Snake\"", json);
        Assert.Contains("\"LoserPicks\"", json);
        Assert.Equal(TurnOrder.Snake, loaded.Draft.BanOrder);
        Assert.Equal(PickOrder.LoserPicks, loaded.Draft.PickOrder);
        Assert.Equal(4, loaded.Draft.SecondBanRoundAfterPicks);
        Assert.True(loaded.Draft.SecondRoundOtherTeamFirst);
    }

    [Fact]
    public void A_pool_file_from_before_draft_rules_loads_with_the_defaults()
    {
        var loaded = JsonSerializer.Deserialize<Mappool>("""{ "Name": "Old" }""", CastoriceJson.Options)!;

        Assert.Equal(1, loaded.Draft.BansPerTeam);
        Assert.Equal(TurnOrder.Alternating, loaded.Draft.BanOrder);
    }

    [Fact]
    public void A_new_pool_has_no_protects()
    {
        var rules = new DraftRules();

        Assert.False(rules.HasProtects);
        Assert.Equal(DraftPhase.Ban, Next(rules, new DraftProgress { PointsToWin = 7 }).Phase);
    }

    [Fact]
    public void A_skipped_protect_counts_as_used_and_the_draft_moves_on()
    {
        var rules = Rules(protects: 1, bans: 1);

        // Red passed on its protect; the page counts that as Red's protect being used.
        var next = Next(rules, new DraftProgress { RedProtects = 1, PointsToWin = 7 });

        Assert.Equal(new DraftTurn(DraftPhase.Protect, B, 2, 2), next);
    }

    [Fact]
    public void Lists_a_skipped_protect_in_the_summary()
    {
        var line = MatchAnnouncer.DraftSummaryLine(
            [("HD1", SlotAvailability.ProtectedByRed)],
            [],
            TeamNames.From("Germany", "Poland"),
            [B]);

        Assert.Equal("Protects: Germany HD1, Poland skipped", line);
        Assert.Equal("Poland skips their protect", MatchAnnouncer.ProtectSkippedLine(B, TeamNames.From("Germany", "Poland")));
    }
}
