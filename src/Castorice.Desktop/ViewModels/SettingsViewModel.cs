using Avalonia.Styling;
using Castorice.Core.Configuration;
using Castorice.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    [ObservableProperty]
    private string _status = string.Empty;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Settings = services.Settings;
        AutoJoinChannels = string.Join(", ", Settings.AutoJoinChannels);
        RefreshImageCacheSummary();
    }

    public AppSettings Settings { get; }

    public string ConfigDirectory => AppPaths.Root;

    public string MappoolDirectory => AppPaths.MappoolDirectory;

    public string ImageCacheDirectory => RemoteImageLoader.CacheDirectory;

    /// <summary>"12 images · 1.4 MB"; recomputed whenever the page is opened or the cache cleared.</summary>
    [ObservableProperty]
    private string _imageCacheSummary = string.Empty;

    public string IrcPasswordHelp =>
        "This is the IRC server password from osu.ppy.sh/home/account/edit (Legacy API), not your account password.";

    public string ApiHelp =>
        "Create an OAuth application at osu.ppy.sh/home/account/edit (OAuth). Only the public scope is used.";

    public IReadOnlyList<string> Themes { get; } = ["Dark", "Light"];

    /// <summary>Comma or space separated; parsed back into the settings list on save.</summary>
    [ObservableProperty]
    private string _autoJoinChannels = string.Empty;

    public string Username
    {
        get => Settings.Username;
        set => SetSetting(value, Settings.Username, v => Settings.Username = v);
    }

    public string IrcPassword
    {
        get => Settings.IrcPassword;
        set => SetSetting(value, Settings.IrcPassword, v => Settings.IrcPassword = v);
    }

    public string IrcHost
    {
        get => Settings.IrcHost;
        set => SetSetting(value, Settings.IrcHost, v => Settings.IrcHost = v);
    }

    public int IrcPort
    {
        get => Settings.IrcPort;
        set => SetSetting(value, Settings.IrcPort, v => Settings.IrcPort = v);
    }

    public bool UseTls
    {
        get => Settings.UseTls;
        set => SetSetting(value, Settings.UseTls, v => Settings.UseTls = v);
    }

    public bool AutoConnect
    {
        get => Settings.AutoConnect;
        set => SetSetting(value, Settings.AutoConnect, v => Settings.AutoConnect = v);
    }

    public bool HasBotAccount
    {
        get => Settings.HasBotAccount;
        set => SetSetting(value, Settings.HasBotAccount, v => Settings.HasBotAccount = v);
    }

    public string OsuClientId
    {
        get => Settings.OsuClientId;
        set => SetSetting(value, Settings.OsuClientId, v => Settings.OsuClientId = v);
    }

    public string OsuClientSecret
    {
        get => Settings.OsuClientSecret;
        set => SetSetting(value, Settings.OsuClientSecret, v => Settings.OsuClientSecret = v);
    }

    public bool ConfirmDestructiveCommands
    {
        get => Settings.ConfirmDestructiveCommands;
        set => SetSetting(value, Settings.ConfirmDestructiveCommands, v => Settings.ConfirmDestructiveCommands = v);
    }

    public string Theme
    {
        get => Settings.Theme;
        set
        {
            if (Settings.Theme == value)
            {
                return;
            }

            Settings.Theme = value;
            OnPropertyChanged();
            ThemeChanged?.Invoke(
                this,
                value.Equals("Light", StringComparison.OrdinalIgnoreCase) ? ThemeVariant.Light : ThemeVariant.Dark);
        }
    }

    public event EventHandler<ThemeVariant>? ThemeChanged;

    public event EventHandler? ApiCredentialsChanged;

    [RelayCommand]
    private void Save()
    {
        Settings.AutoJoinChannels = AutoJoinChannels
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(channel => channel.Trim())
            .Where(channel => channel.Length > 0)
            .Select(channel => channel.StartsWith('#') ? channel : '#' + channel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            _services.SaveSettings();
            _services.Api.Configure(Settings.OsuClientId, Settings.OsuClientSecret);
            ApiCredentialsChanged?.Invoke(this, EventArgs.Empty);

            Status = $"Saved to {_services.SettingsStore.Path}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not save settings: {ex.Message}";
        }
    }

    public void RefreshImageCacheSummary() =>
        ImageCacheSummary = RemoteImageLoader.DiskUsage.Describe();

    [RelayCommand]
    private void ClearImageCache()
    {
        var removed = RemoteImageLoader.ClearCache();
        RefreshImageCacheSummary();

        Status = removed == 0
            ? "The image cache was already empty."
            : $"Cleared {removed} cached {(removed == 1 ? "image" : "images")}. They download again when next shown.";
    }

    [RelayCommand]
    private async Task TestApiAsync()
    {
        if (!Settings.HasApiCredentials)
        {
            Status = "Enter a client id and secret first.";
            return;
        }

        _services.Api.Configure(Settings.OsuClientId, Settings.OsuClientSecret);

        try
        {
            // Any public lookup proves the token exchange works; peppy is user 2.
            var user = await _services.Api.GetUserAsync("2");
            Status = user is null
                ? "The token worked but the test lookup returned nothing."
                : $"API credentials work (resolved {user.Username}).";
        }
        catch (Exception ex)
        {
            Status = $"API test failed: {ex.Message}";
        }
    }

    private void SetSetting<T>(T value, T current, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(value, current))
        {
            return;
        }

        assign(value);
        OnPropertyChanged();
    }
}
