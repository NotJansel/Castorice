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
    }

    private void RebuildGroups()
    {
        Groups.Clear();

        foreach (var group in Pool.Slots.GroupBy(slot => slot.EffectiveCategory))
        {
            Groups.Add(new SlotGroupViewModel(
                group.Key,
                group.Select(slot => new MappoolSlotViewModel(slot))));
        }
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
    private Task KickAsync(RoomPlayerViewModel? player) =>
        player is null
            ? Task.CompletedTask
            : RunAsync(() => _services.Tournament.KickAsync(player.Username), $"Kicked {player.Username}.");

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
            Status = "The match finished. Award the point and pick the next map.";
        }
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
