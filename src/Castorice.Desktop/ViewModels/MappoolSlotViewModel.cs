using Castorice.Core.Tournament;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

public enum DraftMarkKind
{
    /// <summary>A ban or protect.</summary>
    Availability,
    Pick,
}

public sealed class DraftMarkChangedEventArgs(DraftMarkKind kind, bool added) : EventArgs
{
    public DraftMarkKind Kind { get; } = kind;

    /// <summary>True when a mark was set, false when one was cleared.</summary>
    public bool Added { get; } = added;
}

/// <summary>One mappool button. Wraps the model so edits show up without rebuilding the grid.</summary>
public sealed partial class MappoolSlotViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isCurrentPick;

    [ObservableProperty]
    private SlotAvailability _availability = SlotAvailability.Available;

    public MappoolSlotViewModel(MappoolSlot model)
    {
        Model = model;
    }

    public MappoolSlot Model { get; }

    /// <summary>
    /// Ban, protect and pick state belongs to the match, not the pool, so it is never written to
    /// the pool file. Every change flows back through here to the page that owns the match.
    /// </summary>
    public event EventHandler<DraftMarkChangedEventArgs>? DraftMarkChanged;

    /// <summary>When the current ban or protect was set, so the page can undo marks newest first.</summary>
    public long AvailabilityStamp { get; set; }

    /// <summary>When the pick was set; also orders the picks for the draft.</summary>
    public long PickStamp { get; set; }

    public string PickLabel => !IsPicked ? string.Empty : PickedBy switch
    {
        TeamColour.Red => "PICK R",
        TeamColour.Blue => "PICK B",
        _ => "TB",
    };

    /// <summary>Whether the map has been picked in this match; <see cref="PickedBy"/> says by whom.</summary>
    public bool IsPicked { get; private set; }

    /// <summary>The picking team, or <c>null</c> for the tiebreaker.</summary>
    public TeamColour? PickedBy { get; private set; }

    /// <summary>Marks the map as picked. A team of <c>null</c> is the tiebreaker.</summary>
    public void MarkPicked(TeamColour? team)
    {
        if (IsPicked && PickedBy == team)
        {
            return;
        }

        IsPicked = true;
        PickedBy = team;
        RaisePickChanged(added: true);
    }

    public void ClearPick()
    {
        if (!IsPicked)
        {
            return;
        }

        IsPicked = false;
        PickedBy = null;
        RaisePickChanged(added: false);
    }

    private void RaisePickChanged(bool added)
    {
        OnPropertyChanged(nameof(IsPicked));
        OnPropertyChanged(nameof(PickedBy));
        OnPropertyChanged(nameof(PickLabel));
        DraftMarkChanged?.Invoke(this, new DraftMarkChangedEventArgs(DraftMarkKind.Pick, added));
    }

    public bool IsBanned => Availability.IsBanned();

    public bool IsProtected => Availability.IsProtected();

    public bool HasAvailabilityMark => Availability is not SlotAvailability.Available;

    public string AvailabilityLabel => Availability.ShortLabel();

    public TeamColour? AvailabilityTeam => Availability.Team();

    /// <summary>
    /// The pool default this pick falls back to. Set by the page whenever the pool changes, so the
    /// editor can show the effective number rather than a blank box.
    /// </summary>
    public ScoreMultipliers PoolDefault
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(EasyMultiplier));
            OnPropertyChanged(nameof(EasyHiddenMultiplier));
            OnPropertyChanged(nameof(MultiplierOriginLabel));
        }
    } = ScoreMultipliers.None;

    public bool IsFreeMod => Model.Mods.HasFlag(Mods.FreeMod);

    /// <summary>Shows the effective value; writing one turns it into an override for this pick.</summary>
    public double EasyMultiplier
    {
        get => Model.EasyMultiplier ?? PoolDefault.Easy;
        set
        {
            if (Math.Abs(EasyMultiplier - value) < 0.0001)
            {
                return;
            }

            Model.EasyMultiplier = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MultiplierOriginLabel));
        }
    }

    public double EasyHiddenMultiplier
    {
        get => Model.EasyHiddenMultiplier ?? PoolDefault.EasyHidden;
        set
        {
            if (Math.Abs(EasyHiddenMultiplier - value) < 0.0001)
            {
                return;
            }

            Model.EasyHiddenMultiplier = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MultiplierOriginLabel));
        }
    }

    public string MultiplierOriginLabel => Model.HasMultiplierOverride ? "custom" : "pool default";

    [RelayCommand]
    private void UsePoolMultipliers()
    {
        Model.ClearMultiplierOverride();
        OnPropertyChanged(nameof(EasyMultiplier));
        OnPropertyChanged(nameof(EasyHiddenMultiplier));
        OnPropertyChanged(nameof(MultiplierOriginLabel));
    }

    [RelayCommand]
    private void BanRed() => Availability = SlotAvailability.BannedByRed;

    [RelayCommand]
    private void BanBlue() => Availability = SlotAvailability.BannedByBlue;

    [RelayCommand]
    private void ProtectRed() => Availability = SlotAvailability.ProtectedByRed;

    [RelayCommand]
    private void ProtectBlue() => Availability = SlotAvailability.ProtectedByBlue;

    [RelayCommand]
    private void PickedByRed() => MarkPicked(TeamColour.Red);

    [RelayCommand]
    private void PickedByBlue() => MarkPicked(TeamColour.Blue);

    [RelayCommand]
    private void ClearAvailability() => Availability = SlotAvailability.Available;

    [RelayCommand]
    private void ClearPickMark() => ClearPick();

    partial void OnAvailabilityChanged(SlotAvailability value)
    {
        OnPropertyChanged(nameof(IsBanned));
        OnPropertyChanged(nameof(IsProtected));
        OnPropertyChanged(nameof(HasAvailabilityMark));
        OnPropertyChanged(nameof(AvailabilityLabel));
        OnPropertyChanged(nameof(AvailabilityTeam));
        DraftMarkChanged?.Invoke(
            this,
            new DraftMarkChangedEventArgs(DraftMarkKind.Availability, value is not SlotAvailability.Available));
    }

    public string Label
    {
        get => Model.Label;
        set => SetModel(value, Model.Label, v => Model.Label = v, nameof(Label), nameof(Category));
    }

    public long BeatmapId
    {
        get => Model.BeatmapId;
        set => SetModel(value, Model.BeatmapId, v => Model.BeatmapId = v, nameof(BeatmapId), nameof(BeatmapUrl));
    }

    public string Title
    {
        get => Model.Title;
        set => SetModel(value, Model.Title, v => Model.Title = v, nameof(Title), nameof(DisplayName));
    }

    public string Artist
    {
        get => Model.Artist;
        set => SetModel(value, Model.Artist, v => Model.Artist = v, nameof(Artist), nameof(Subtitle));
    }

    public string Difficulty
    {
        get => Model.Difficulty;
        set => SetModel(value, Model.Difficulty, v => Model.Difficulty = v, nameof(Difficulty), nameof(DisplayName));
    }

    public string Mapper
    {
        get => Model.Mapper;
        set => SetModel(value, Model.Mapper, v => Model.Mapper = v, nameof(Mapper), nameof(Subtitle));
    }

    public double StarRating
    {
        get => Model.StarRating;
        set => SetModel(value, Model.StarRating, v => Model.StarRating = v, nameof(StarRating), nameof(Metadata));
    }

    public double Bpm
    {
        get => Model.Bpm;
        set => SetModel(value, Model.Bpm, v => Model.Bpm = v, nameof(Bpm), nameof(Metadata));
    }

    public int LengthSeconds
    {
        get => Model.LengthSeconds;
        set => SetModel(value, Model.LengthSeconds, v => Model.LengthSeconds = v, nameof(LengthSeconds), nameof(Metadata));
    }

    public string CoverUrl
    {
        get => Model.CoverUrl;
        set => SetModel(value, Model.CoverUrl, v => Model.CoverUrl = v, nameof(CoverUrl), nameof(HasCover));
    }

    public bool HasCover => Model.CoverUrl.Length > 0;

    public string Notes
    {
        get => Model.Notes;
        set => SetModel(value, Model.Notes, v => Model.Notes = v, nameof(Notes));
    }

    /// <summary>Editable as text so hand-written mod strings such as "HDHR" work.</summary>
    public string ModsText
    {
        get => Model.Mods is Mods.None ? string.Empty : Model.Mods.ToCompactAcronyms();
        set
        {
            var parsed = ModsExtensions.ParseMods(value);
            if (parsed == Model.Mods)
            {
                return;
            }

            Model.Mods = parsed;
            OnPropertyChanged(nameof(ModsText));
            OnPropertyChanged(nameof(ModsBadge));
        }
    }

    public string ModsBadge => Model.Mods.ToCompactAcronyms();

    public string Category => Model.EffectiveCategory;

    public string DisplayName => Model.DisplayName;

    public string Subtitle => string.Join(
        " · ",
        new[] { Model.Artist, Model.Mapper.Length > 0 ? $"mapped by {Model.Mapper}" : string.Empty }
            .Where(part => part.Length > 0));

    /// <summary>Star rating alone, for the tile's corner chip.</summary>
    public string StarDisplay => Model.StarRating > 0 ? $"{Model.StarRating:0.00}\u2605" : string.Empty;

    public string Metadata
    {
        get
        {
            var parts = new List<string>(3);
            if (Model.StarRating > 0)
            {
                parts.Add($"{Model.StarRating:0.00}★");
            }

            if (Model.Bpm > 0)
            {
                parts.Add($"{Model.Bpm:0} BPM");
            }

            if (Model.LengthSeconds > 0)
            {
                parts.Add(Model.DisplayLength);
            }

            return string.Join("  ", parts);
        }
    }

    public string BeatmapUrl => Model.BeatmapId > 0 ? $"https://osu.ppy.sh/b/{Model.BeatmapId}" : string.Empty;

    /// <summary>Re-raises every derived property after the model was changed from outside.</summary>
    public void RefreshAll()
    {
        OnPropertyChanged(string.Empty);
    }

    private void SetModel<T>(T value, T current, Action<T> assign, params string[] changed)
    {
        if (EqualityComparer<T>.Default.Equals(value, current))
        {
            return;
        }

        assign(value);
        foreach (var name in changed)
        {
            OnPropertyChanged(name);
        }
    }
}
