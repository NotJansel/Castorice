using Castorice.Core.Tournament;

namespace Castorice.Core.Bancho;

/// <summary>A BanchoBot line recognised as something the room state can act on.</summary>
public abstract record BanchoEvent
{
    /// <summary>The line this event was parsed from.</summary>
    public string Raw { get; init; } = string.Empty;
}

/// <summary>Answer to <c>!mp make</c>, carrying the match id needed to rejoin the room.</summary>
public sealed record MatchCreated(long MatchId, string RoomName) : BanchoEvent
{
    public string ChannelName => $"#mp_{MatchId}";

    public string HistoryUrl => $"https://osu.ppy.sh/mp/{MatchId}";
}

/// <summary>The <c>Room name: …, History: …</c> header of <c>!mp settings</c>.</summary>
public sealed record RoomHeader(string RoomName, long MatchId) : BanchoEvent;

public sealed record BeatmapChanged(long BeatmapId, string Artist, string Title, string Difficulty) : BanchoEvent;

public sealed record ModsChanged(Mods Mods) : BanchoEvent;

public sealed record MatchSettingsChanged(TeamMode TeamMode, ScoreMode ScoreMode, int? Size) : BanchoEvent;

public sealed record PlayerJoined(string Username, int Slot, TeamColour? Team) : BanchoEvent;

public sealed record PlayerLeft(string Username) : BanchoEvent;

public sealed record PlayerMoved(string Username, int Slot) : BanchoEvent;

public sealed record PlayerTeamChanged(string Username, TeamColour Team) : BanchoEvent;

/// <summary>A <c>Slot N</c> row from <c>!mp settings</c>.</summary>
public sealed record SlotReport(int Slot, string Status, long UserId, string Username, TeamColour? Team, Mods Mods)
    : BanchoEvent;

public sealed record PlayerScore(string Username, long Score, bool Passed) : BanchoEvent;

public sealed record MatchStarted : BanchoEvent;

public sealed record MatchFinished : BanchoEvent;

public sealed record MatchAborted : BanchoEvent;

public sealed record MatchClosed : BanchoEvent;

public sealed record AllPlayersReady : BanchoEvent;

public sealed record CountdownFinished : BanchoEvent;

public sealed record HostChanged(string Username) : BanchoEvent;
