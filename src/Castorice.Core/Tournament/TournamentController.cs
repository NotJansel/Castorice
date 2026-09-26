using Castorice.Core.Bancho;
using Castorice.Core.Chat;
using Castorice.Core.Irc;

namespace Castorice.Core.Tournament;

/// <summary>
/// The referee's remote control: one method per button in the tournament panel. It owns the
/// currently attached room, sends <c>!mp</c> commands through the rate-limited IRC client, and
/// feeds BanchoBot's replies back into <see cref="MultiplayerRoom"/>.
/// </summary>
public sealed class TournamentController
{
    private readonly IrcClient _client;
    private readonly ChatService _chat;
    private readonly IUiDispatcher _dispatcher;

    public TournamentController(IrcClient client, ChatService chat, IUiDispatcher? dispatcher = null)
    {
        _client = client;
        _chat = chat;
        _dispatcher = dispatcher ?? IUiDispatcher.Immediate;
        _client.MessageReceived += OnIrcMessage;
    }

    /// <summary>The room commands are sent to, or <c>null</c> when no lobby is attached.</summary>
    public MultiplayerRoom? Room { get; private set; }

    public Mappool? Pool { get; set; }

    /// <summary>The slot most recently pushed to the lobby, for the "currently picked" highlight.</summary>
    public MappoolSlot? CurrentPick { get; private set; }

    public bool IsAttached => Room is not null;

    public event EventHandler<MultiplayerRoom?>? RoomChanged;

    public event EventHandler<BanchoEvent>? RoomEvent;

    /// <summary>
    /// Asks BanchoBot to open a lobby. The match id only arrives in BanchoBot's reply, so this
    /// returns once the room is attached or the wait times out.
    /// </summary>
    public async Task<MultiplayerRoom?> CreateRoomAsync(
        string roomName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);

        var created = new TaskCompletionSource<MatchCreated>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, IrcMessage message)
        {
            if (!IsFromBanchoBot(message))
            {
                return;
            }

            if (BanchoBotParser.Parse(message.Trailing) is MatchCreated evt)
            {
                created.TrySetResult(evt);
            }
        }

        _client.MessageReceived += Handler;
        try
        {
            await _client.SendMessageAsync(ChatTarget.BanchoBot, MpCommands.Make(roomName), cancellationToken)
                .ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            await using var registration = cts.Token.Register(
                static s => ((TaskCompletionSource<MatchCreated>)s!).TrySetCanceled(),
                created);

            var result = await created.Task.ConfigureAwait(false);
            await AttachAsync(result.MatchId, cancellationToken).ConfigureAwait(false);

            if (Pool is not null)
            {
                await ApplyPoolSettingsAsync(cancellationToken).ConfigureAwait(false);
            }

            return Room;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _chat.AppendClientNotice(
                _chat.Server,
                "BanchoBot did not confirm the new lobby in time. If it was created, attach to it by match id.");
            return null;
        }
        finally
        {
            _client.MessageReceived -= Handler;
        }
    }

    /// <summary>Joins an existing <c>#mp_*</c> channel and starts tracking it.</summary>
    public async Task AttachAsync(long matchId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(matchId);

        var room = new MultiplayerRoom(matchId, _dispatcher);
        Room = room;
        _dispatcher.Post(() => RoomChanged?.Invoke(this, room));

        _chat.Open(room.ChannelName);
        await _client.JoinAsync(room.ChannelName, cancellationToken).ConfigureAwait(false);
        await SendAsync(MpCommands.Settings(), cancellationToken).ConfigureAwait(false);
    }

    public void Detach()
    {
        Room = null;
        CurrentPick = null;
        _dispatcher.Post(() => RoomChanged?.Invoke(this, null));
    }

    /// <summary>Pushes <c>!mp set</c> and the referee list from the configured pool.</summary>
    public async Task ApplyPoolSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (Pool is null)
        {
            return;
        }

        foreach (var command in MpCommands.ConfigureRoom(Pool))
        {
            await SendAsync(command, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sets the map and its mods — the action behind every mappool button.</summary>
    public async Task PickAsync(MappoolSlot slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (slot.BeatmapId <= 0)
        {
            _chat.AppendClientNotice(
                _chat.Server,
                $"Slot \"{slot.Label}\" has no beatmap id, so it cannot be picked.");
            return;
        }

        var mods = Pool?.ModsToSend(slot) ?? slot.Mods;
        foreach (var command in MpCommands.PickSlot(slot, Pool?.PlayMode ?? PlayMode.Osu, mods))
        {
            await SendAsync(command, cancellationToken).ConfigureAwait(false);
        }

        CurrentPick = slot;
    }

    public Task StartAsync(int? seconds = null, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Start(seconds ?? Pool?.StartTimerSeconds ?? 10), cancellationToken);

    public Task StartNowAsync(CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Start(), cancellationToken);

    public Task AbortStartTimerAsync(CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.AbortStartTimer(), cancellationToken);

    public Task AbortAsync(CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Abort(), cancellationToken);

    public Task TimerAsync(int? seconds = null, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Timer(seconds ?? Pool?.ReadyTimerSeconds ?? 120), cancellationToken);

    public Task RefreshSettingsAsync(CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Settings(), cancellationToken);

    public Task InviteAsync(string username, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Invite(username), cancellationToken);

    public Task KickAsync(string username, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Kick(username), cancellationToken);

    public Task SetTeamAsync(string username, TeamColour team, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Team(username, team), cancellationToken);

    /// <summary>Moves a player into a specific slot, 1-based.</summary>
    public Task MoveAsync(string username, int slot, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Move(username, slot), cancellationToken);

    public Task LockAsync(CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Lock(), cancellationToken);

    public Task UnlockAsync(CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Unlock(), cancellationToken);

    public Task ClearHostAsync(CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.ClearHost(), cancellationToken);

    public Task SetPasswordAsync(string? password, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.Password(password), cancellationToken);

    public Task SetModsAsync(Mods mods, CancellationToken cancellationToken = default) =>
        SendAsync(MpCommands.SetMods(mods), cancellationToken);

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(MpCommands.Close(), cancellationToken).ConfigureAwait(false);
        Detach();
    }

    /// <summary>Sends one command into the attached room's channel.</summary>
    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        var room = Room;
        if (room is null)
        {
            _chat.AppendClientNotice(_chat.Server, $"No lobby is attached, so \"{command}\" was not sent.");
            return;
        }

        var target = _chat.Open(room.ChannelName);
        await _chat.SendAsync(target, command, cancellationToken).ConfigureAwait(false);
    }

    private void OnIrcMessage(object? sender, IrcMessage message)
    {
        var room = Room;
        if (room is null || message.Command is not "PRIVMSG" || !IsFromBanchoBot(message))
        {
            return;
        }

        // Accept the room's own channel, plus the PM where "!mp make" is answered.
        var destination = message.ParameterAt(0);
        if (destination.StartsWith('#') &&
            !destination.Equals(room.ChannelName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (room.Apply(message.Trailing) is { } evt)
        {
            _dispatcher.Post(() => RoomEvent?.Invoke(this, evt));
        }
    }

    private static bool IsFromBanchoBot(IrcMessage message) =>
        string.Equals(message.Nick, ChatTarget.BanchoBot, StringComparison.OrdinalIgnoreCase);
}
