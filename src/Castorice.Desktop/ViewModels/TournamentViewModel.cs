using System.Collections.ObjectModel;
using System.Globalization;
using Castorice.Core.Bancho;
using Castorice.Core.Tournament;
using Castorice.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

/// <summary>Mappool picks and referee controls for the attached lobby.</summary>
public sealed partial class TournamentViewModel : ViewModelBase
{
    private readonly AppServices _services;

    // Set while re-selecting the file the pool was just written to, so the save does not bounce
    // back through a reload and throw away the in-memory state.
    private bool _suppressPoolReload;

    // BanchoBot reports the per-player scores around the "match has finished" line rather than
    // before it, so scoring waits for the lobby to go quiet instead of reading a half-filled room.
    private static readonly TimeSpan ScoreSettleDelay = TimeSpan.FromSeconds(4);

    private CancellationTokenSource? _scoringCts;

    [ObservableProperty]
    private MappoolFile? _selectedPoolFile;

    [ObservableProperty]
    private Mappool _pool = new();

    [ObservableProperty]
    private MappoolSlotViewModel? _selectedSlot;

    [ObservableProperty]
    private string _redTeam = string.Empty;

    [ObservableProperty]
    private string _blueTeam = string.Empty;

    [ObservableProperty]
    private string _attachMatchId = string.Empty;

    [ObservableProperty]
    private string _inviteName = string.Empty;

    [ObservableProperty]
    private string _status = "No lobby attached.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _newSlotLabel = string.Empty;

    [ObservableProperty]
    private string _newSlotBeatmapId = string.Empty;

    [ObservableProperty]
    private string _newSlotMods = string.Empty;

    /// <summary>Warmups must not score, so a match starts in warmup until the referee says otherwise.</summary>
    [ObservableProperty]
    private bool _isWarmup = true;

    [ObservableProperty]
    private bool _autoScore = true;

    /// <summary>Check the lobby against the pool's FreeMod rule once everyone is ready.</summary>
    [ObservableProperty]
    private bool _autoFreeModCheck = true;

    public TournamentViewModel(AppServices services)
    {
        _services = services;

        _services.Tournament.RoomChanged += (_, room) => OnRoomChanged(room);
        _services.Tournament.RoomEvent += (_, evt) => OnRoomEvent(evt);

        RefreshPoolList();

        if (_services.Settings.LastMappoolFile is { Length: > 0 } last)
        {
            SelectedPoolFile = PoolFiles.FirstOrDefault(f =>
                f.FileName.Equals(last, StringComparison.OrdinalIgnoreCase));
        }

        SelectedPoolFile ??= PoolFiles.FirstOrDefault();
        if (SelectedPoolFile is null)
        {
            ApplyPool(SampleMappool());
        }
    }

    public ObservableCollection<MappoolFile> PoolFiles { get; } = [];

    /// <summary>Picks grouped by bracket, so the UI can lay out one row per category.</summary>
    public ObservableCollection<SlotGroupViewModel> Groups { get; } = [];

    /// <summary>The FreeMod picks, which are the only ones that carry a score multiplier.</summary>
    public ObservableCollection<MappoolSlotViewModel> FreeModSlots { get; } = [];

    public bool HasFreeModSlots => FreeModSlots.Count > 0;

    public ObservableCollection<string> RoomLog { get; } = [];

    /// <summary>
    /// The pool's own fields are wrapped rather than bound through <c>Pool.Name</c> directly:
    /// <see cref="Mappool"/> raises no change notifications, so a rename would otherwise update the
    /// box it was typed into and nothing else.
    /// </summary>
    public string PoolName
    {
        get => Pool.Name;
        set
        {
            if (Pool.Name == value)
            {
                return;
            }

            Pool.Name = value;
            OnPropertyChanged();
        }
    }

    public string PoolAcronym
    {
        get => Pool.Acronym;
        set
        {
            if (Pool.Acronym == value)
            {
                return;
            }

            Pool.Acronym = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPoolAcronym));
        }
    }

    public bool HasPoolAcronym => !string.IsNullOrWhiteSpace(Pool.Acronym);

    public string PoolStage
    {
        get => Pool.Stage;
        set
        {
            if (Pool.Stage == value)
            {
                return;
            }

            Pool.Stage = value;
            OnPropertyChanged();
        }
    }

    public int BestOf
    {
        get => Pool.BestOf;
        set
        {
            if (Pool.BestOf == value)
            {
                return;
            }

            Pool.BestOf = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PointsToWin));
            OnPropertyChanged(nameof(MatchTargetDisplay));
        }
    }

    public double EasyMultiplier
    {
        get => Pool.EasyMultiplier;
        set
        {
            if (Math.Abs(Pool.EasyMultiplier - value) < 0.0001)
            {
                return;
            }

            Pool.EasyMultiplier = value;
            OnPropertyChanged();
            PushPoolDefaultsToSlots();
        }
    }

    public double EasyHiddenMultiplier
    {
        get => Pool.EasyHiddenMultiplier;
        set
        {
            if (Math.Abs(Pool.EasyHiddenMultiplier - value) < 0.0001)
            {
                return;
            }

            Pool.EasyHiddenMultiplier = value;
            OnPropertyChanged();
            PushPoolDefaultsToSlots();
        }
    }

    public int PointsToWin => Pool.PointsToWin;

    public string MatchTargetDisplay => $"First to {Pool.PointsToWin}";

    public MultiplayerRoom? Room => _services.Tournament.Room;

    public bool IsAttached => Room is not null;

    public string RoomTitle => Room is null ? "No lobby" : $"{Room.RoomName} ({Room.ChannelName})";

    public string CurrentMapDisplay => Room?.CurrentBeatmapName is { Length: > 0 } name
        ? name
        : "No beatmap set";

    public string CurrentModsDisplay => Room?.CurrentMods.ToCompactAcronyms() ?? "-";

    public ObservableCollection<RoomPlayerViewModel> Players { get; } = [];

    public int RedScore
    {
        get => Room?.RedScore ?? 0;
        set
        {
            if (Room is null || Room.RedScore == value)
            {
                return;
            }

            Room.RedScore = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public int BlueScore
    {
        get => Room?.BlueScore ?? 0;
        set
        {
            if (Room is null || Room.BlueScore == value)
            {
                return;
            }

            Room.BlueScore = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<TeamMode> TeamModes { get; } = Enum.GetValues<TeamMode>();

    public IReadOnlyList<ScoreMode> ScoreModes { get; } = Enum.GetValues<ScoreMode>();

    public IReadOnlyList<PlayMode> PlayModes { get; } = Enum.GetValues<PlayMode>();

    partial void OnSelectedPoolFileChanged(MappoolFile? value)
    {
        if (value is null || _suppressPoolReload)
        {
            return;
        }

        var loaded = _services.Mappools.Load(value.FileName);
        if (loaded is null)
        {
            Status = $"Could not read {value.FileName}.";
            return;
        }

        ApplyPool(loaded);

        _services.Settings.LastMappoolFile = value.FileName;
        _services.SaveSettings();
    }

    public void RefreshPoolList()
    {
        var previous = SelectedPoolFile?.FileName;

        PoolFiles.Clear();
        foreach (var file in _services.Mappools.List())
        {
            PoolFiles.Add(file);
        }

        if (previous is not null)
        {
            SelectedPoolFile = PoolFiles.FirstOrDefault(f =>
                f.FileName.Equals(previous, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ApplyPool(Mappool pool)
    {
        Pool = pool;
        _services.Tournament.Pool = pool;
        RebuildGroups();

        OnPropertyChanged(nameof(Pool));
        OnPropertyChanged(nameof(PoolName));
        OnPropertyChanged(nameof(PoolAcronym));
        OnPropertyChanged(nameof(HasPoolAcronym));
        OnPropertyChanged(nameof(PoolStage));
        OnPropertyChanged(nameof(BestOf));
        OnPropertyChanged(nameof(PointsToWin));
        OnPropertyChanged(nameof(MatchTargetDisplay));
        OnPropertyChanged(nameof(EasyMultiplier));
        OnPropertyChanged(nameof(EasyHiddenMultiplier));
    }

    private void RebuildGroups()
    {
        Groups.Clear();

        foreach (var group in Pool.Slots.GroupBy(slot => slot.EffectiveCategory))
        {
            var slots = group.Select(slot =>
            {
                var viewModel = new MappoolSlotViewModel(slot);
                viewModel.AvailabilityChanged += OnSlotAvailabilityChanged;
                return viewModel;
            });

            Groups.Add(new SlotGroupViewModel(group.Key, slots));
        }

        FreeModSlots.Clear();
        foreach (var slot in AllSlots.Where(s => s.IsFreeMod))
        {
            FreeModSlots.Add(slot);
        }

        PushPoolDefaultsToSlots();

        OnPropertyChanged(nameof(BanSummary));
        OnPropertyChanged(nameof(HasFreeModSlots));
    }

    /// <summary>Keeps every pick's fallback in step with the pool-level default.</summary>
    private void PushPoolDefaultsToSlots()
    {
        var defaults = Pool.Multipliers;
        foreach (var slot in FreeModSlots)
        {
            slot.PoolDefault = defaults;
        }
    }

    private void OnSlotAvailabilityChanged(object? sender, EventArgs e)
    {
        if (sender is MappoolSlotViewModel slot)
        {
            Status = slot.Availability is SlotAvailability.Available
                ? $"Cleared {slot.Label}."
                : $"{slot.Label}: {slot.AvailabilityLabel}.";
        }

        OnPropertyChanged(nameof(BanSummary));
    }

    /// <summary>One line naming every ban and protect, for the lobby panel.</summary>
    public string BanSummary
    {
        get
        {
            var marked = AllSlots.Where(s => s.HasAvailabilityMark).ToList();
            return marked.Count == 0
                ? "No bans or protects yet."
                : string.Join("   ", marked.Select(s => $"{s.Label} {s.AvailabilityLabel}"));
        }
    }

    [RelayCommand]
    private void ClearBansAndProtects()
    {
        foreach (var slot in AllSlots)
        {
            slot.Availability = SlotAvailability.Available;
        }

        Status = "Cleared every ban and protect.";
    }

    private IEnumerable<MappoolSlotViewModel> AllSlots => Groups.SelectMany(group => group.Slots);

    // ---- lobby lifecycle -------------------------------------------------

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        if (!_services.Irc.IsConnected)
        {
            Status = "Connect to IRC first.";
            return;
        }

        IsBusy = true;
        Status = "Asking BanchoBot for a lobby…";
        try
        {
            var name = Pool.BuildRoomName(RedTeam, BlueTeam);
            var room = await _services.Tournament.CreateRoomAsync(name);
            Status = room is null
                ? "BanchoBot did not confirm the lobby. Check the private message with BanchoBot."
                : $"Created {room.ChannelName}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not create the lobby: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AttachAsync()
    {
        var text = AttachMatchId.Trim();

        // Accept a bare id, a #mp_ channel, or a full match history URL.
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var matchId) || matchId <= 0)
        {
            Status = "Enter a match id, a #mp_ channel, or an osu.ppy.sh/mp link.";
            return;
        }

        IsBusy = true;
        try
        {
            await _services.Tournament.AttachAsync(matchId);
            Status = $"Attached to #mp_{matchId}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not attach: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Detach()
    {
        _services.Tournament.Detach();
        Status = "Detached from the lobby. It is still open on Bancho.";
    }

    [RelayCommand]
    private Task CloseRoomAsync() => RunAsync(
        () => _services.Tournament.CloseAsync(),
        "Closed the lobby.");

    [RelayCommand]
    private Task ApplySettingsAsync() => RunAsync(
        () => _services.Tournament.ApplyPoolSettingsAsync(),
        "Pushed the pool's room settings.");

    // ---- referee controls ------------------------------------------------

    [RelayCommand]
    private Task PickAsync(MappoolSlotViewModel? slot)
    {
        if (slot is null)
        {
            return Task.CompletedTask;
        }

        if (slot.IsBanned)
        {
            // Left as a refusal rather than a disabled tile, so the ban stays easy to take back.
            Status = $"{slot.Label} is banned by {slot.AvailabilityTeam}. Clear the ban to pick it.";
            return Task.CompletedTask;
        }

        return RunAsync(
            async () =>
            {
                await _services.Tournament.PickAsync(slot.Model);

                foreach (var candidate in AllSlots)
                {
                    candidate.IsCurrentPick = ReferenceEquals(candidate, slot);
                }
            },
            $"Picked {slot.Label}.");
    }

    [RelayCommand]
    private Task StartAsync() => RunAsync(
        () => _services.Tournament.StartAsync(),
        $"Starting in {Pool.StartTimerSeconds}s.");

    [RelayCommand]
    private Task StartNowAsync() => RunAsync(
        () => _services.Tournament.StartNowAsync(),
        "Started the match.");

    [RelayCommand]
    private Task AbortTimerAsync() => RunAsync(
        () => _services.Tournament.AbortStartTimerAsync(),
        "Aborted the countdown.");

    [RelayCommand]
    private Task AbortAsync() => RunAsync(
        () => _services.Tournament.AbortAsync(),
        "Aborted the match.");

    [RelayCommand]
    private Task TimerAsync() => RunAsync(
        () => _services.Tournament.TimerAsync(),
        $"Started a {Pool.ReadyTimerSeconds}s timer.");

    [RelayCommand]
    private Task RefreshRoomAsync() => RunAsync(
        () => _services.Tournament.RefreshSettingsAsync(),
        "Requested !mp settings.");

    [RelayCommand]
    private Task LockAsync() => RunAsync(() => _services.Tournament.LockAsync(), "Locked the slots.");

    [RelayCommand]
    private Task UnlockAsync() => RunAsync(() => _services.Tournament.UnlockAsync(), "Unlocked the slots.");

    [RelayCommand]
    private Task ClearHostAsync() => RunAsync(
        () => _services.Tournament.ClearHostAsync(),
        "Cleared the host.");

    [RelayCommand]
    private Task InviteAsync()
    {
        var name = InviteName.Trim();
        if (name.Length == 0)
        {
            return Task.CompletedTask;
        }

        InviteName = string.Empty;
        return RunAsync(() => _services.Tournament.InviteAsync(name), $"Invited {name}.");
    }

    [RelayCommand]
    private Task InviteRefereesAsync()
    {
        if (Pool.Referees.Count == 0)
        {
            Status = "The pool has no referees configured.";
            return Task.CompletedTask;
        }

        return RunAsync(
            async () =>
            {
                foreach (var referee in Pool.Referees)
                {
                    await _services.Tournament.InviteAsync(referee);
                }
            },
            $"Invited {Pool.Referees.Count} referee(s).");
    }

    [RelayCommand]
    private Task SetTeamAsync(RoomPlayerViewModel? player)
    {
        if (player is null)
        {
            return Task.CompletedTask;
        }

        // Toggle: whichever team they are not on now.
        var next = player.Team is TeamColour.Red ? TeamColour.Blue : TeamColour.Red;
        return RunAsync(
            () => _services.Tournament.SetTeamAsync(player.Username, next),
            $"Moved {player.Username} to {next}.");
    }

    [RelayCommand]
    private Task MoveAsync(RoomPlayerViewModel? player) =>
        player is null
            ? Task.CompletedTask
            : RunAsync(
                () => _services.Tournament.MoveAsync(player.Username, player.TargetSlot),
                $"Moved {player.Username} to slot {player.TargetSlot}.");

    [RelayCommand]
    private Task KickAsync(RoomPlayerViewModel? player) =>
        player is null
            ? Task.CompletedTask
            : RunAsync(() => _services.Tournament.KickAsync(player.Username), $"Kicked {player.Username}.");

    /// <summary>
    /// Refreshes the lobby, then checks every player against the pool's FreeMod rule. Only
    /// meaningful on a FreeMod pick, where players choose their own mods.
    /// </summary>
    [RelayCommand]
    private async Task CheckFreeModAsync()
    {
        if (!_services.Tournament.IsAttached)
        {
            Status = "No lobby is attached.";
            return;
        }

        try
        {
            await _services.Tournament.RefreshSettingsAsync();
            await Task.Delay(ScoreSettleDelay);
            await RunFreeModCheckAsync(announce: true);
        }
        catch (Exception ex)
        {
            Status = $"FreeMod check failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns true when the lobby passes. With <paramref name="announce"/> the outcome goes into
    /// the lobby; the automatic run only speaks up when something is actually wrong.
    /// </summary>
    private async Task<bool> RunFreeModCheckAsync(bool announce, CancellationToken cancellationToken = default)
    {
        var room = Room;
        if (room is null)
        {
            return true;
        }

        if (!room.CurrentMods.HasFlag(Mods.FreeMod))
        {
            Status = "The current pick is not FreeMod, so there is nothing to check.";
            return true;
        }

        var inputs = room.Players
            .Select(p => new PlayerScoreInput(p.Username, p.Team, 0, true, p.Mods))
            .ToList();

        var result = FreeModCheck.Check(inputs, Pool.FreeModAllowedMods, Pool.FreeModRequiresAMod);

        if (!result.HasData)
        {
            Status = "Nobody's mods are known yet — run Refresh settings first.";
            return true;
        }

        Status = result.Summary;

        if (announce || !result.IsClean)
        {
            await _services.Tournament.SendAsync(result.Summary, cancellationToken);
        }

        return result.IsClean;
    }

    [RelayCommand]
    private void AddRedPoint() => RedScore++;

    [RelayCommand]
    private void AddBluePoint() => BlueScore++;

    [RelayCommand]
    private void ResetScore()
    {
        RedScore = 0;
        BlueScore = 0;
    }

    // ---- mappool editing -------------------------------------------------

    [RelayCommand]
    private void AddSlot()
    {
        var label = NewSlotLabel.Trim();
        if (label.Length == 0)
        {
            Status = "Give the pick a label, e.g. NM1.";
            return;
        }

        var digits = new string(NewSlotBeatmapId.Where(char.IsDigit).ToArray());
        _ = long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var beatmapId);

        Pool.Slots.Add(new MappoolSlot
        {
            Label = label,
            BeatmapId = beatmapId,
            Mods = ModsExtensions.ParseMods(NewSlotMods),
        });

        NewSlotLabel = string.Empty;
        NewSlotBeatmapId = string.Empty;
        NewSlotMods = string.Empty;

        RebuildGroups();
        Status = $"Added {label}. Save the pool to keep it.";
    }

    [RelayCommand]
    private void RemoveSlot(MappoolSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        Pool.Slots.Remove(slot.Model);
        if (ReferenceEquals(SelectedSlot, slot))
        {
            SelectedSlot = null;
        }

        RebuildGroups();
        Status = $"Removed {slot.Label}. Save the pool to keep it.";
    }

    [RelayCommand]
    private void NewPool()
    {
        ApplyPool(new Mappool { Name = "New Mappool" });
        SelectedPoolFile = null;
        Status = "Started a new pool. Save it to create the file.";
    }

    [RelayCommand]
    private void SavePool()
    {
        try
        {
            var fileName = _services.Mappools.Save(Pool, SelectedPoolFile?.FileName);

            // Re-selecting the file would otherwise reload it and rebuild every slot view model,
            // dropping the "currently picked" highlight in the middle of a match.
            _suppressPoolReload = true;
            try
            {
                RefreshPoolList();
                SelectedPoolFile = PoolFiles.FirstOrDefault(f =>
                    f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressPoolReload = false;
            }

            // Normally recorded by the selection handler, which the guard above just skipped.
            _services.Settings.LastMappoolFile = fileName;
            _services.SaveSettings();

            Status = $"Saved \"{Pool.Name}\" to {fileName}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not save the pool: {ex.Message}";
        }
    }

    /// <summary>Fills in title, mapper, star rating, BPM and length for every pick from the osu! API.</summary>
    [RelayCommand]
    private async Task FetchMetadataAsync()
    {
        if (!_services.Api.IsConfigured)
        {
            Status = "Add osu! API credentials in Settings to fetch beatmap metadata.";
            return;
        }

        var ids = Pool.Slots.Where(s => s.BeatmapId > 0).Select(s => s.BeatmapId).ToList();
        if (ids.Count == 0)
        {
            Status = "No picks have a beatmap id yet.";
            return;
        }

        IsBusy = true;
        Status = $"Fetching metadata for {ids.Count} beatmap(s)…";
        try
        {
            var beatmaps = (await _services.Api.GetBeatmapsAsync(ids)).ToDictionary(b => b.Id);
            var matched = 0;

            foreach (var slotVm in AllSlots)
            {
                if (!beatmaps.TryGetValue(slotVm.Model.BeatmapId, out var beatmap))
                {
                    continue;
                }

                var slot = slotVm.Model;
                slot.Difficulty = beatmap.Version;
                slot.StarRating = beatmap.DifficultyRating;
                slot.Bpm = beatmap.Bpm ?? 0;
                slot.LengthSeconds = beatmap.HitLength > 0 ? beatmap.HitLength : beatmap.TotalLength;

                if (beatmap.Beatmapset is { } set)
                {
                    slot.Title = set.Title;
                    slot.Artist = set.Artist;
                    slot.Mapper = set.Creator;

                    // "card" is the widest cover the tiles can use without wasting bandwidth.
                    slot.CoverUrl = set.Covers?.Card ?? set.Covers?.Cover ?? string.Empty;
                }

                slotVm.RefreshAll();
                matched++;
            }

            Status = matched == ids.Count
                ? $"Updated {matched} pick(s)."
                : $"Updated {matched} of {ids.Count} pick(s); the rest were not found.";
        }
        catch (Exception ex)
        {
            Status = $"Metadata lookup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- plumbing --------------------------------------------------------

    private async Task RunAsync(Func<Task> action, string successStatus)
    {
        if (!_services.Tournament.IsAttached)
        {
            Status = "No lobby is attached.";
            return;
        }

        try
        {
            await action();
            Status = successStatus;
        }
        catch (Exception ex)
        {
            Status = $"Command failed: {ex.Message}";
        }
    }

    private void OnRoomChanged(MultiplayerRoom? room)
    {
        Players.Clear();

        OnPropertyChanged(nameof(Room));
        OnPropertyChanged(nameof(IsAttached));
        OnPropertyChanged(nameof(RoomTitle));
        OnPropertyChanged(nameof(CurrentMapDisplay));
        OnPropertyChanged(nameof(CurrentModsDisplay));
        OnPropertyChanged(nameof(RedScore));
        OnPropertyChanged(nameof(BlueScore));

        if (room is null)
        {
            RoomLog.Clear();
        }
    }

    private void OnRoomEvent(BanchoEvent evt)
    {
        RoomLog.Add($"{DateTimeOffset.Now:HH:mm:ss}  {evt.Raw}");
        while (RoomLog.Count > 300)
        {
            RoomLog.RemoveAt(0);
        }

        SyncPlayers();

        OnPropertyChanged(nameof(RoomTitle));
        OnPropertyChanged(nameof(CurrentMapDisplay));
        OnPropertyChanged(nameof(CurrentModsDisplay));

        if (evt is MatchFinished)
        {
            _ = HandleMatchFinishedAsync();
        }

        // "All players are ready" is the last moment a mod problem can still be fixed cheaply.
        if (evt is AllPlayersReady && AutoFreeModCheck && !IsWarmup)
        {
            _ = RunFreeModCheckAsync(announce: false);
        }
    }

    /// <summary>
    /// Runs once per finished map. Warmups are skipped outright, and with auto-scoring off this
    /// only nudges the referee — the point is never awarded behind their back.
    /// </summary>
    private async Task HandleMatchFinishedAsync()
    {
        if (IsWarmup)
        {
            Status = "Warmup finished — not scored. Turn Warmup off when the match starts.";
            return;
        }

        if (!AutoScore)
        {
            Status = "Map finished. Award the point and pick the next map.";
            return;
        }

        // Null on the first finished map of a session, so this cannot be an unconditional await.
        if (_scoringCts is not null)
        {
            await _scoringCts.CancelAsync().ConfigureAwait(true);
            _scoringCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _scoringCts = cts;

        try
        {
            Status = "Map finished — collecting scores…";

            // Refreshes each player's team and mods, which the FreeMod multipliers depend on.
            await _services.Tournament.RefreshSettingsAsync(cts.Token);
            await Task.Delay(ScoreSettleDelay, cts.Token);

            await ScoreFinishedMapAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // A second map finished first; that run owns the scoring now.
        }
        catch (Exception ex)
        {
            Status = $"Could not score the map: {ex.Message}";
        }
    }

    private async Task ScoreFinishedMapAsync(CancellationToken cancellationToken)
    {
        var room = Room;
        if (room is null)
        {
            return;
        }

        var inputs = room.Players
            .Select(p => new PlayerScoreInput(p.Username, p.Team, p.LastScore ?? 0, p.LastScorePassed, p.Mods))
            .ToList();

        // The multipliers belong to the pick that is on the board, not to the pool as a whole.
        var pick = _services.Tournament.CurrentPick;
        var result = MatchScoring.Score(inputs, room.CurrentMods, Pool.MultipliersFor(pick));

        if (!result.HasScores)
        {
            Status = "The map finished but no scores came through — award the point manually.";
            return;
        }

        if (!result.HasTeams)
        {
            Status = "The map finished but the lobby has no teams, so no point was awarded.";
        }

        switch (result.Winner)
        {
            case TeamColour.Red:
                room.RedScore++;
                break;
            case TeamColour.Blue:
                room.BlueScore++;
                break;
        }

        OnPropertyChanged(nameof(RedScore));
        OnPropertyChanged(nameof(BlueScore));

        var standing = new MatchStanding(
            room.RedScore,
            room.BlueScore,
            Pool.PointsToWin,
            RedTeam.Trim() is { Length: > 0 } red ? red : "Red",
            BlueTeam.Trim() is { Length: > 0 } blue ? blue : "Blue");

        var messages = MatchAnnouncer.BuildResultMessages(
            result,
            pick?.Label ?? string.Empty,
            room.CurrentBeatmapName,
            standing);

        foreach (var message in messages)
        {
            await _services.Tournament.SendAsync(message, cancellationToken);
        }

        Status = result.Winner is { } winner
            ? $"{winner} takes the map. Match: {room.RedScore} - {room.BlueScore}."
            : $"The map was tied at {result.RedTotal:N0}. No point awarded.";
    }

    private void SyncPlayers()
    {
        var room = Room;
        if (room is null)
        {
            Players.Clear();
            return;
        }

        // The room list is small, so rebuilding is simpler than diffing and just as fast.
        var wanted = room.Players.OrderBy(p => p.Slot).ToList();

        Players.Clear();
        foreach (var player in wanted)
        {
            Players.Add(new RoomPlayerViewModel(player));
        }
    }

    /// <summary>A small pool so a first run has something on screen before any file exists.</summary>
    private static Mappool SampleMappool() => new()
    {
        Name = "Sample Pool",
        Acronym = "CAST",
        Stage = "Qualifiers",
        Slots =
        [
            new MappoolSlot { Label = "NM1", Category = "NoMod", Mods = Mods.None },
            new MappoolSlot { Label = "NM2", Category = "NoMod", Mods = Mods.None },
            new MappoolSlot { Label = "HD1", Category = "Hidden", Mods = Mods.Hidden },
            new MappoolSlot { Label = "HR1", Category = "HardRock", Mods = Mods.HardRock },
            new MappoolSlot { Label = "DT1", Category = "DoubleTime", Mods = Mods.DoubleTime },
            new MappoolSlot { Label = "FM1", Category = "FreeMod", Mods = Mods.FreeMod },
            new MappoolSlot { Label = "TB", Category = "Tiebreaker", Mods = Mods.FreeMod },
        ],
    };
}

/// <summary>A bracket row in the mappool grid, e.g. all HD picks.</summary>
public sealed class SlotGroupViewModel(string name, IEnumerable<MappoolSlotViewModel> slots)
{
    public string Name { get; } = name;

    public IReadOnlyList<MappoolSlotViewModel> Slots { get; } = slots.ToList();
}
