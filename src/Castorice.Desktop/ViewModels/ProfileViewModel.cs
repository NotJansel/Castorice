using System.Collections.ObjectModel;
using System.Globalization;
using Castorice.Core.Osu;
using Castorice.Core.Osu.Models;
using Castorice.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

public sealed partial class ProfileViewModel : ViewModelBase
{
    private static readonly GameModeOption[] Modes =
    [
        new("osu", "osu!"),
        new("taiko", "osu!taiko"),
        new("fruits", "osu!catch"),
        new("mania", "osu!mania"),
    ];

    private readonly AppServices _services;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private GameModeOption _selectedMode = Modes[0];

    [ObservableProperty]
    private OsuUser? _user;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    public ProfileViewModel(AppServices services)
    {
        _services = services;
        Query = services.Settings.Username;
    }

    public IReadOnlyList<GameModeOption> AvailableModes { get; } = Modes;

    public ObservableCollection<OsuScore> TopPlays { get; } = [];

    public ObservableCollection<OsuScore> RecentPlays { get; } = [];

    public bool HasUser => User is not null;

    public bool IsApiConfigured => _services.Api.IsConfigured;

    public string SelectedModeDisplay => SelectedMode.Display;

    // ---- derived display strings ----------------------------------------

    public string GlobalRankDisplay => Format(User?.Statistics?.GlobalRank, "#");

    public string CountryRankDisplay => Format(User?.Statistics?.CountryRank, "#");

    public string PpDisplay => User?.Statistics is { } s ? $"{s.Pp:N0}pp" : "-";

    public string AccuracyDisplay => User?.Statistics is { } s ? $"{s.Accuracy:0.00}%" : "-";

    public string PlayCountDisplay => User?.Statistics is { } s ? s.PlayCount.ToString("N0", CultureInfo.CurrentCulture) : "-";

    public string PlayTimeDisplay
    {
        get
        {
            if (User?.Statistics is not { } s || s.PlayTimeSeconds is null or 0)
            {
                return "-";
            }

            var time = s.PlayTime;
            return $"{(int)time.TotalHours}h {time.Minutes}m";
        }
    }

    public string LevelDisplay => User?.Statistics?.Level is { } level
        ? $"{level.Current} ({level.Progress}%)"
        : "-";

    public string MaxComboDisplay => User?.Statistics is { } s && s.MaximumCombo > 0
        ? $"{s.MaximumCombo:N0}x"
        : "-";

    public string RankedScoreDisplay => User?.Statistics is { } s
        ? s.RankedScore.ToString("N0", CultureInfo.CurrentCulture)
        : "-";

    public string ReplaysWatchedDisplay => User?.Statistics is { } s
        ? s.ReplaysWatched.ToString("N0", CultureInfo.CurrentCulture)
        : "-";

    public string JoinedDisplay => User?.JoinDate is { } joined
        ? joined.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
        : "-";

    public string LastSeenDisplay => User is null
        ? "-"
        : User.IsOnline
            ? "Online now"
            : User.LastVisit is { } visit
                ? visit.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "Hidden";

    public string GradeSsDisplay => Grade(g => g.Ss + g.SilverSs);

    public string GradeSDisplay => Grade(g => g.S + g.SilverS);

    public string GradeADisplay => Grade(g => g.A);

    public string CountryDisplay => User?.Country?.Name ?? User?.CountryCode ?? string.Empty;

    /// <summary>90-day global rank history, normalised into a 0-1 sparkline series (higher is better).</summary>
    public IReadOnlyList<double> RankSparkline
    {
        get
        {
            var data = User?.RankHistory?.Data;
            if (data is not { Count: > 1 })
            {
                return [];
            }

            var min = data.Min();
            var max = data.Max();
            if (max == min)
            {
                return data.Select(_ => 0.5).ToList();
            }

            // Ranks count down, so invert to make "up" mean "better".
            return data.Select(rank => 1.0 - ((double)(rank - min) / (max - min))).ToList();
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = Query.Trim();
        if (query.Length == 0)
        {
            return;
        }

        if (!_services.Api.IsConfigured)
        {
            Error = "Add an osu! API client id and secret in Settings to use the profile view.";
            return;
        }

        IsLoading = true;
        Error = null;
        try
        {
            var user = await _services.Api.GetUserAsync(query, SelectedMode.Key);
            if (user is null)
            {
                Error = $"No osu! user called \"{query}\".";
                SetUser(null);
                return;
            }

            SetUser(user);

            var top = await _services.Api.GetUserScoresAsync(user.Id, ScoreType.Best, SelectedMode.Key, 10);
            var recent = await _services.Api.GetUserScoresAsync(
                user.Id,
                ScoreType.Recent,
                SelectedMode.Key,
                10,
                includeFails: true);

            TopPlays.Clear();
            foreach (var score in top)
            {
                TopPlays.Add(score);
            }

            RecentPlays.Clear();
            foreach (var score in recent)
            {
                RecentPlays.Add(score);
            }
        }
        catch (OsuApiException ex)
        {
            Error = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            Error = $"Could not reach the osu! API: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task ShowMeAsync()
    {
        Query = _services.Settings.Username;
        return SearchAsync();
    }

    /// <summary>Opens a profile from elsewhere in the app, e.g. a click in the lobby player list.</summary>
    public Task ShowAsync(string username)
    {
        Query = username.Replace('_', ' ');
        return SearchAsync();
    }

    public void NotifyApiConfigurationChanged() => OnPropertyChanged(nameof(IsApiConfigured));

    partial void OnSelectedModeChanged(GameModeOption value)
    {
        OnPropertyChanged(nameof(SelectedModeDisplay));

        if (User is not null)
        {
            _ = SearchAsync();
        }
    }

    private void SetUser(OsuUser? user)
    {
        User = user;

        // Every stat string derives from User, so refresh them in one go.
        OnPropertyChanged(string.Empty);
    }

    private string Grade(Func<OsuGradeCounts, int> select) =>
        User?.Statistics?.GradeCounts is { } counts
            ? select(counts).ToString("N0", CultureInfo.CurrentCulture)
            : "-";

    private static string Format(int? value, string prefix) =>
        value is null or 0 ? "-" : prefix + value.Value.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>A ruleset the profile view can be switched to.</summary>
public sealed record GameModeOption(string Key, string Display)
{
    public override string ToString() => Display;
}
