using Castorice.Core.Tournament;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Castorice.Desktop.ViewModels;

/// <summary>One mappool button. Wraps the model so edits show up without rebuilding the grid.</summary>
public sealed partial class MappoolSlotViewModel(MappoolSlot model) : ViewModelBase
{
    [ObservableProperty]
    private bool _isCurrentPick;

    public MappoolSlot Model { get; } = model;

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
