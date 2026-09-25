namespace Castorice.Core.Tournament;

/// <summary>
/// Which lines the tournament panel posts into the lobby on its own. Buttons the referee presses
/// to post something always post; these only govern what happens automatically.
/// </summary>
public sealed class LobbyAnnouncements
{
    /// <summary>The finished map's team totals and winner.</summary>
    public bool MapResult { get; set; } = true;

    /// <summary>The Easy multipliers applied on a FreeMod pick.</summary>
    public bool Multipliers { get; set; } = true;

    /// <summary>The running match score, or the match winner, after a scored map.</summary>
    public bool MatchScore { get; set; } = true;

    /// <summary>The match score after a point awarded with the +1 buttons.</summary>
    public bool ScoreOnManualPoint { get; set; } = true;

    /// <summary>Each protect, ban and pick as it is marked, e.g. <c>Red bans NM2</c>.</summary>
    public bool DraftActions { get; set; } = true;

    /// <summary>Whose turn it is once a protect or ban is marked or a map is scored.</summary>
    public bool NextTurn { get; set; } = true;

    /// <summary>The automatic FreeMod check's warning when someone's mods are off.</summary>
    public bool FreeModWarnings { get; set; } = true;
}
