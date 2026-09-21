using System.Collections.ObjectModel;
using Castorice.Core.Chat;
using Castorice.Core.Tournament;

namespace Castorice.Core.Bancho;

public sealed class RoomPlayer
{
    public required string Username { get; init; }

    public long UserId { get; set; }

    public int Slot { get; set; }

    public TeamColour? Team { get; set; }

    public Mods Mods { get; set; } = Mods.None;

    public string Status { get; set; } = string.Empty;

    /// <summary>Score from the most recent finished map; <c>null</c> until one is reported.</summary>
    public long? LastScore { get; set; }

    public bool LastScorePassed { get; set; }
}

public enum RoomState
{
    Idle,
    Playing,
    Finished,
}

/// <summary>
/// Live picture of one <c>#mp_*</c> room, rebuilt from BanchoBot's own announcements so it stays
/// correct even when a second referee is issuing commands.
/// </summary>
public sealed class MultiplayerRoom
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ObservableCollection<RoomPlayer> _players = [];

    public MultiplayerRoom(long matchId, IUiDispatcher? dispatcher = null)
    {
        MatchId = matchId;
        _dispatcher = dispatcher ?? IUiDispatcher.Immediate;
        Players = new ReadOnlyObservableCollection<RoomPlayer>(_players);
    }

    public long MatchId { get; private set; }

    public string ChannelName => $"#mp_{MatchId}";

    public string HistoryUrl => $"https://osu.ppy.sh/mp/{MatchId}";

    public string RoomName { get; private set; } = string.Empty;

    public ReadOnlyObservableCollection<RoomPlayer> Players { get; }

    public long? CurrentBeatmapId { get; private set; }

    public string CurrentBeatmapName { get; private set; } = string.Empty;

    public Mods CurrentMods { get; private set; } = Mods.None;

    public TeamMode TeamMode { get; private set; } = TeamMode.HeadToHead;

    public ScoreMode ScoreMode { get; private set; } = ScoreMode.Score;

    public RoomState State { get; private set; } = RoomState.Idle;

    /// <summary>Maps won per team, incremented by the caller after each map is scored.</summary>
    public int RedScore { get; set; }

    public int BlueScore { get; set; }

    public event EventHandler? Changed;

    public event EventHandler<BanchoEvent>? EventApplied;

    /// <summary>Applies one BanchoBot line. Returns the recognised event, or <c>null</c>.</summary>
    public BanchoEvent? Apply(string banchoLine)
    {
        var evt = BanchoBotParser.Parse(banchoLine);
        if (evt is null)
        {
            return null;
        }

        _dispatcher.Post(() =>
        {
            ApplyCore(evt);
            EventApplied?.Invoke(this, evt);
            Changed?.Invoke(this, EventArgs.Empty);
        });

        return evt;
    }

    public RoomPlayer? FindPlayer(string username) =>
        _players.FirstOrDefault(p => string.Equals(
            p.Username.Replace(' ', '_'),
            username.Replace(' ', '_'),
            StringComparison.OrdinalIgnoreCase));

    /// <summary>Sum of the last reported scores for one team.</summary>
    public long TeamScore(TeamColour team) =>
        _players.Where(p => p.Team == team).Sum(p => p.LastScore ?? 0);

    private void ApplyCore(BanchoEvent evt)
    {
        switch (evt)
        {
            case MatchCreated created:
                MatchId = created.MatchId;
                RoomName = created.RoomName;
                break;

            case RoomHeader header:
                MatchId = header.MatchId;
                RoomName = header.RoomName;
                break;

            case BeatmapChanged beatmap:
                CurrentBeatmapId = beatmap.BeatmapId;
                CurrentBeatmapName = string.IsNullOrEmpty(beatmap.Difficulty)
                    ? $"{beatmap.Artist} - {beatmap.Title}"
                    : $"{beatmap.Artist} - {beatmap.Title} [{beatmap.Difficulty}]";
                break;

            case ModsChanged mods:
                CurrentMods = mods.Mods;
                break;

            case MatchSettingsChanged settings:
                TeamMode = settings.TeamMode;
                ScoreMode = settings.ScoreMode;
                break;

            case SlotReport report:
                {
                    var player = Upsert(report.Username);
                    player.Slot = report.Slot;
                    player.UserId = report.UserId;
                    player.Team = report.Team;
                    player.Mods = report.Mods;
                    player.Status = report.Status;
                    break;
                }

            case PlayerJoined joined:
                {
                    var player = Upsert(joined.Username);
                    player.Slot = joined.Slot;
                    player.Team = joined.Team;
                    break;
                }

            case PlayerLeft left:
                {
                    var player = FindPlayer(left.Username);
                    if (player is not null)
                    {
                        _players.Remove(player);
                    }

                    break;
                }

            case PlayerMoved moved:
                Upsert(moved.Username).Slot = moved.Slot;
                break;

            case PlayerTeamChanged teamChanged:
                Upsert(teamChanged.Username).Team = teamChanged.Team;
                break;

            case PlayerScore score:
                {
                    var player = Upsert(score.Username);
                    player.LastScore = score.Score;
                    player.LastScorePassed = score.Passed;
                    break;
                }

            case MatchStarted:
                State = RoomState.Playing;
                foreach (var player in _players)
                {
                    player.LastScore = null;
                }

                break;

            case MatchFinished:
                State = RoomState.Finished;
                break;

            case MatchAborted:
                State = RoomState.Idle;
                break;
        }
    }

    private RoomPlayer Upsert(string username)
    {
        var existing = FindPlayer(username);
        if (existing is not null)
        {
            return existing;
        }

        var player = new RoomPlayer { Username = username };
        _players.Add(player);
        return player;
    }
}
