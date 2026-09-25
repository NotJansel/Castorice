using System.Text.Json.Serialization;

namespace Castorice.Core.Tournament;

/// <summary>How the two teams take turns within one protect or ban round.</summary>
public enum TurnOrder
{
    /// <summary>A B A B: each team in turn.</summary>
    Alternating,

    /// <summary>A B B A: the second team takes two in a row, then the first team does.</summary>
    Snake,
}

/// <summary>Who picks the next map.</summary>
public enum PickOrder
{
    /// <summary>A B A B, starting with the first-pick team.</summary>
    Alternating,

    /// <summary>A B B A A B B A, starting with the first-pick team.</summary>
    Snake,

    /// <summary>The first-pick team opens; after that the team that lost the last map picks.</summary>
    LoserPicks,

    /// <summary>The first-pick team opens; after that the team that won the last map picks.</summary>
    WinnerPicks,
}

public enum DraftPhase
{
    Protect,
    Ban,
    Pick,
    Tiebreaker,
    Finished,
}

/// <summary>
/// A bracket's protect, ban and pick order. Stored with the pool, since it is part of the rules
/// of the stage rather than of one match.
/// </summary>
public sealed class DraftRules
{
    public int ProtectsPerTeam { get; set; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter<TurnOrder>))]
    public TurnOrder ProtectOrder { get; set; } = TurnOrder.Alternating;

    /// <summary>Bans each team makes before the first pick.</summary>
    public int BansPerTeam { get; set; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter<TurnOrder>))]
    public TurnOrder BanOrder { get; set; } = TurnOrder.Alternating;

    [JsonConverter(typeof(JsonStringEnumConverter<PickOrder>))]
    public PickOrder PickOrder { get; set; } = PickOrder.Alternating;

    /// <summary>
    /// Picks made before a second ban round, e.g. 4 for "ban, pick four, ban again". 0 means the
    /// bracket bans only once.
    /// </summary>
    public int SecondBanRoundAfterPicks { get; set; }

    /// <summary>Bans each team makes in the second round. Ignored without a second round.</summary>
    public int SecondRoundBansPerTeam { get; set; } = 1;

    /// <summary>
    /// Off: the second round opens with the first-ban team again. On: with the other team.
    /// </summary>
    public bool SecondRoundOtherTeamFirst { get; set; }

    [JsonIgnore]
    public bool HasSecondBanRound => SecondBanRoundAfterPicks > 0 && SecondRoundBansPerTeam > 0;

    public DraftRules Clone() => (DraftRules)MemberwiseClone();
}

/// <summary>Which team opens each phase. Settled per match, usually by the roll.</summary>
public sealed record DraftStart(TeamColour FirstProtect, TeamColour FirstBan, TeamColour FirstPick)
{
    public static DraftStart Red { get; } = new(TeamColour.Red, TeamColour.Red, TeamColour.Red);
}

/// <summary>What has happened in the match so far, as far as the draft is concerned.</summary>
public sealed record DraftProgress
{
    public int RedProtects { get; init; }

    public int BlueProtects { get; init; }

    public int RedBans { get; init; }

    public int BlueBans { get; init; }

    /// <summary>The picking team of every pick, oldest first; <c>null</c> for the tiebreaker.</summary>
    public IReadOnlyList<TeamColour?> Picks { get; init; } = [];

    /// <summary>The winner of every scored map, oldest first; <c>null</c> for a tie.</summary>
    public IReadOnlyList<TeamColour?> MapWinners { get; init; } = [];

    public int RedScore { get; init; }

    public int BlueScore { get; init; }

    public int PointsToWin { get; init; } = 1;
}

/// <summary>
/// One action in the draft. <see cref="Team"/> is <c>null</c> for the tiebreaker, once the match
/// is over, and for a loser/winner pick while the map deciding it is still being played.
/// </summary>
public sealed record DraftTurn(DraftPhase Phase, TeamColour? Team, int Number = 0, int Total = 0, int Round = 1)
{
    public static DraftTurn Finished { get; } = new(DraftPhase.Finished, null);

    /// <summary>A protect or ban, which the referee marks on a tile rather than sending to the lobby.</summary>
    public bool IsMark => Phase is DraftPhase.Protect or DraftPhase.Ban;

    /// <summary>For the referee's own panel, e.g. <c>Blue bans · 2 of 4</c>.</summary>
    public string Describe(TeamNames names, PickOrder pickOrder)
    {
        ArgumentNullException.ThrowIfNull(names);

        var count = Total > 0 ? $" · {Number} of {Total}" : string.Empty;
        var round = Round > 1 ? " (second round)" : string.Empty;

        return Phase switch
        {
            DraftPhase.Protect => $"{names.For(Team)} protects{count}",
            DraftPhase.Ban => $"{names.For(Team)} bans{round}{count}",
            DraftPhase.Pick when Team is { } team => $"{names.For(team)} picks",
            DraftPhase.Pick => pickOrder is PickOrder.WinnerPicks
                ? "The winner of this map picks next"
                : "The loser of this map picks next",
            DraftPhase.Tiebreaker => "Tiebreaker",
            _ => "Match over",
        };
    }
}

/// <summary>Display names for the two teams, falling back to the colour.</summary>
public sealed record TeamNames(string Red, string Blue)
{
    public static TeamNames Default { get; } = new("Red", "Blue");

    public static TeamNames From(string? red, string? blue) => new(
        string.IsNullOrWhiteSpace(red) ? "Red" : red.Trim(),
        string.IsNullOrWhiteSpace(blue) ? "Blue" : blue.Trim());

    public string For(TeamColour? team) => team switch
    {
        TeamColour.Red => Red,
        TeamColour.Blue => Blue,
        _ => "?",
    };
}

/// <summary>Where the draft stands: the next action overall, and whose pick the next pick is.</summary>
public sealed record DraftState(DraftTurn Next, TeamColour? NextPicker);

/// <summary>
/// Works out whose turn it is from what has been marked so far.
/// </summary>
/// <remarks>
/// The expected sequence is walked step by step, and each step is ticked off against that team's
/// own count rather than against a position. So when a referee marks a ban for the wrong team, the
/// draft does not shift everyone after it: the step the other team still owes stays the next one.
/// </remarks>
public static class DraftOrder
{
    public static TeamColour Other(this TeamColour team) =>
        team is TeamColour.Red ? TeamColour.Blue : TeamColour.Red;

    /// <summary>The team taking turn <paramref name="index"/> (0-based) of a round.</summary>
    public static TeamColour TeamAt(TurnOrder order, TeamColour first, int index) =>
        TeamAt(order is TurnOrder.Snake, first, index);

    public static DraftState Evaluate(DraftRules rules, DraftStart start, DraftProgress progress)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(progress);

        var protects = new Tally(progress.RedProtects, progress.BlueProtects);
        var bans = new Tally(progress.RedBans, progress.BlueBans);
        var teamPicks = new Tally(
            progress.Picks.Count(t => t is TeamColour.Red),
            progress.Picks.Count(t => t is TeamColour.Blue));
        var picksMade = teamPicks.Red + teamPicks.Blue;
        var dynamicPicksLeft = picksMade;

        DraftTurn? next = null;

        foreach (var step in Steps(rules, start))
        {
            if (step.Phase is DraftPhase.Protect or DraftPhase.Ban)
            {
                var tally = step.Phase is DraftPhase.Protect ? protects : bans;
                if (!tally.TryTake(step.Team!.Value))
                {
                    next ??= step;
                }

                continue;
            }

            // A pick step. Fixed orders tick off per team; loser/winner picks only by count.
            if (step.Team is { } team)
            {
                if (teamPicks.TryTake(team))
                {
                    continue;
                }
            }
            else if (dynamicPicksLeft > 0)
            {
                dynamicPicksLeft--;
                continue;
            }

            var picker = step.Team ?? DynamicPicker(rules.PickOrder, start, progress, picksMade);
            next ??= step with { Team = picker };
            return new DraftState(ApplyScore(next, progress), picker);
        }

        // Steps() never ends, so the loop always returns.
        throw new InvalidOperationException("The draft sequence ended unexpectedly.");
    }

    /// <summary>A score reaching the target ends the draft; one point short each calls the tiebreaker.</summary>
    private static DraftTurn ApplyScore(DraftTurn next, DraftProgress progress)
    {
        var target = Math.Max(1, progress.PointsToWin);

        if (progress.RedScore >= target || progress.BlueScore >= target)
        {
            return DraftTurn.Finished;
        }

        if (target > 1 && progress.RedScore == target - 1 && progress.BlueScore == target - 1)
        {
            return new DraftTurn(DraftPhase.Tiebreaker, null);
        }

        return next;
    }

    private static TeamColour? DynamicPicker(
        PickOrder order,
        DraftStart start,
        DraftProgress progress,
        int picksMade)
    {
        if (picksMade == 0)
        {
            return start.FirstPick;
        }

        // The last pick has not been played out yet, so its result cannot decide anything.
        if (progress.MapWinners.Count < picksMade)
        {
            return null;
        }

        if (progress.MapWinners[^1] is { } winner)
        {
            return order is PickOrder.WinnerPicks ? winner : winner.Other();
        }

        // A tied map decides nothing, so the turn passes to the other team.
        var lastPicker = progress.Picks.LastOrDefault(t => t is not null);
        return lastPicker?.Other() ?? start.FirstPick;
    }

    /// <summary>
    /// The full expected sequence: protects, bans, picks, with the second ban round slotted in after
    /// the configured number of picks. Picks go on for ever; the score decides when it ends.
    /// </summary>
    private static IEnumerable<DraftTurn> Steps(DraftRules rules, DraftStart start)
    {
        var protectCount = Math.Max(0, rules.ProtectsPerTeam) * 2;
        for (var i = 0; i < protectCount; i++)
        {
            yield return new DraftTurn(
                DraftPhase.Protect,
                TeamAt(rules.ProtectOrder, start.FirstProtect, i),
                i + 1,
                protectCount);
        }

        var banCount = Math.Max(0, rules.BansPerTeam) * 2;
        for (var i = 0; i < banCount; i++)
        {
            yield return new DraftTurn(DraftPhase.Ban, TeamAt(rules.BanOrder, start.FirstBan, i), i + 1, banCount);
        }

        var dynamic = rules.PickOrder is PickOrder.LoserPicks or PickOrder.WinnerPicks;
        var snakePicks = rules.PickOrder is PickOrder.Snake;

        for (var pick = 0; ; pick++)
        {
            if (rules.HasSecondBanRound && pick == rules.SecondBanRoundAfterPicks)
            {
                var first = rules.SecondRoundOtherTeamFirst ? start.FirstBan.Other() : start.FirstBan;
                var count = rules.SecondRoundBansPerTeam * 2;
                for (var i = 0; i < count; i++)
                {
                    yield return new DraftTurn(DraftPhase.Ban, TeamAt(rules.BanOrder, first, i), i + 1, count, Round: 2);
                }
            }

            yield return new DraftTurn(
                DraftPhase.Pick,
                dynamic ? null : TeamAt(snakePicks, start.FirstPick, pick),
                pick + 1);
        }
    }

    private static TeamColour TeamAt(bool snake, TeamColour first, int index)
    {
        // Snake: A B B A A B B A — the pair index flips every two turns, offset by one.
        var firstTeamsTurn = snake ? ((index + 1) / 2) % 2 == 0 : index % 2 == 0;
        return firstTeamsTurn ? first : first.Other();
    }

    private sealed class Tally(int red, int blue)
    {
        public int Red { get; private set; } = red;

        public int Blue { get; private set; } = blue;

        public bool TryTake(TeamColour team)
        {
            if (team is TeamColour.Red && Red > 0)
            {
                Red--;
                return true;
            }

            if (team is TeamColour.Blue && Blue > 0)
            {
                Blue--;
                return true;
            }

            return false;
        }
    }
}
