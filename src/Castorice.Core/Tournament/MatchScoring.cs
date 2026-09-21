namespace Castorice.Core.Tournament;

/// <summary>What a team did to a pick during the ban/protect phase.</summary>
public enum SlotAvailability
{
    Available,
    BannedByRed,
    BannedByBlue,
    ProtectedByRed,
    ProtectedByBlue,
}

public static class SlotAvailabilityExtensions
{
    public static bool IsBanned(this SlotAvailability availability) =>
        availability is SlotAvailability.BannedByRed or SlotAvailability.BannedByBlue;

    public static bool IsProtected(this SlotAvailability availability) =>
        availability is SlotAvailability.ProtectedByRed or SlotAvailability.ProtectedByBlue;

    /// <summary>The team that acted, or <c>null</c> for <see cref="SlotAvailability.Available"/>.</summary>
    public static TeamColour? Team(this SlotAvailability availability) => availability switch
    {
        SlotAvailability.BannedByRed or SlotAvailability.ProtectedByRed => TeamColour.Red,
        SlotAvailability.BannedByBlue or SlotAvailability.ProtectedByBlue => TeamColour.Blue,
        _ => null,
    };

    public static string ShortLabel(this SlotAvailability availability) => availability switch
    {
        SlotAvailability.BannedByRed => "BAN R",
        SlotAvailability.BannedByBlue => "BAN B",
        SlotAvailability.ProtectedByRed => "PROT R",
        SlotAvailability.ProtectedByBlue => "PROT B",
        _ => string.Empty,
    };
}

/// <summary>
/// Score multipliers applied to players who take a handicap mod on a FreeMod pick. 1.75x for Easy
/// is the value most brackets use; it is configurable per pool because the rules differ.
/// </summary>
public sealed record ScoreMultipliers(double Easy = 1.75, double EasyHidden = 1.75)
{
    /// <summary>No adjustment at all.</summary>
    public static ScoreMultipliers None { get; } = new(1.0, 1.0);

    /// <summary>
    /// The factor for one player's mods. Easy combined with Hidden gets its own value, since
    /// Hidden already carries a score bonus of its own under ScoreV2.
    /// </summary>
    public double For(Mods playerMods)
    {
        if (!playerMods.HasFlag(Mods.Easy))
        {
            return 1.0;
        }

        return playerMods.HasFlag(Mods.Hidden) ? EasyHidden : Easy;
    }
}

/// <summary>One player's contribution, as handed to the scorer.</summary>
public sealed record PlayerScoreInput(string Username, TeamColour? Team, long Score, bool Passed, Mods Mods);

public sealed record PlayerScoreLine(
    string Username,
    TeamColour? Team,
    long RawScore,
    bool Passed,
    Mods Mods,
    double Multiplier)
{
    /// <summary>A failed score counts as nothing, which is how tournament rules treat it.</summary>
    public long AdjustedScore => Passed
        ? (long)Math.Round(RawScore * Multiplier, MidpointRounding.AwayFromZero)
        : 0;

    public bool WasAdjusted => Passed && RawScore > 0 && Math.Abs(Multiplier - 1.0) > 0.0001;
}

public sealed record MapResult(IReadOnlyList<PlayerScoreLine> Players, long RedTotal, long BlueTotal)
{
    /// <summary>False when no player reported a score, i.e. there is nothing to award a point on.</summary>
    public bool HasScores => Players.Any(p => p.RawScore > 0);

    public bool HasTeams => Players.Any(p => p.Team is not null);

    /// <summary><c>null</c> on a draw, or when the lobby is not playing in teams.</summary>
    public TeamColour? Winner => !HasTeams || RedTotal == BlueTotal
        ? null
        : RedTotal > BlueTotal ? TeamColour.Red : TeamColour.Blue;

    public long Margin => Math.Abs(RedTotal - BlueTotal);

    public IEnumerable<PlayerScoreLine> Adjusted => Players.Where(p => p.WasAdjusted);
}

public static class MatchScoring
{
    /// <summary>
    /// Totals one map. Multipliers only apply on a FreeMod pick — on a forced-mod pick every player
    /// is on the same mods, so there is nothing to even out.
    /// </summary>
    public static MapResult Score(
        IEnumerable<PlayerScoreInput> players,
        Mods slotMods,
        ScoreMultipliers? multipliers = null)
    {
        ArgumentNullException.ThrowIfNull(players);

        var freeMod = slotMods.HasFlag(Mods.FreeMod);
        var table = freeMod ? multipliers ?? ScoreMultipliers.None : ScoreMultipliers.None;

        var lines = players
            .Select(p => new PlayerScoreLine(p.Username, p.Team, p.Score, p.Passed, p.Mods, table.For(p.Mods)))
            .ToList();

        return new MapResult(
            lines,
            lines.Where(p => p.Team is TeamColour.Red).Sum(p => p.AdjustedScore),
            lines.Where(p => p.Team is TeamColour.Blue).Sum(p => p.AdjustedScore));
    }
}
