using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Castorice.Core.Updates;
using Castorice.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

/// <summary>
/// Looks for a new release now and then and offers it in a banner. Installing downloads the
/// installer for this system and hands it to <see cref="UpdateInstaller"/>.
/// </summary>
public sealed partial class UpdateViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly AppServices _services;
    private readonly UpdateChecker _checker = new(AppInfo.GitHubRepository);
    private readonly CancellationTokenSource _lifetime = new();

    private UpdateInfo? _update;
    private bool _disposed;

    // Dismissed with the ×: not offered again until the next start.
    private string? _dismissedVersion;

    [ObservableProperty]
    private bool _isBannerVisible;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _bannerText = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isChecking;

    public UpdateViewModel(AppServices services)
    {
        _services = services;

        if (AppInfo.IsDevelopmentBuild)
        {
            _status = $"Development build {AppInfo.DisplayVersion}: it only looks for updates when asked.";
        }
    }

    /// <summary>"Update now" when Castorice can install the update itself, "Download" otherwise.</summary>
    public string InstallLabel => CanInstallInApp ? "Update now" : "Download";

    private bool CanInstallInApp => _update?.Asset is not null && UpdateInstaller.CanInstallInPlace;

    public bool CheckForUpdates
    {
        get => _services.Settings.CheckForUpdates;
        set
        {
            if (_services.Settings.CheckForUpdates == value)
            {
                return;
            }

            _services.Settings.CheckForUpdates = value;
            TrySaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Checks shortly after start and then every few hours, while checking is switched on. A
    /// development build leaves it to the button in Settings: every release would pass for newer.
    /// </summary>
    public void Start()
    {
        if (!AppInfo.IsDevelopmentBuild)
        {
            _ = RunPeriodicChecksAsync(_lifetime.Token);
        }
    }

    private async Task RunPeriodicChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (CheckForUpdates && !IsBannerVisible)
                {
                    await CheckAsync(announceResult: false, cancellationToken);
                }

                await Task.Delay(CheckInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    [RelayCommand]
    private Task CheckNowAsync() => CheckAsync(announceResult: true, _lifetime.Token);

    /// <summary>
    /// The automatic check stays silent unless there is something to offer; the button in
    /// Settings also says when everything is current or the check failed.
    /// </summary>
    private async Task CheckAsync(bool announceResult, CancellationToken cancellationToken)
    {
        if (IsChecking || IsDownloading)
        {
            return;
        }

        var current = ReleaseVersion.TryParse(AppInfo.Version);
        if (current is null)
        {
            Status = $"This build's version ({AppInfo.Version}) cannot be compared with releases.";
            return;
        }

        IsChecking = true;
        if (announceResult)
        {
            Status = "Checking for updates…";
        }

        try
        {
            var update = await _checker.CheckAsync(current, UpdateInstaller.Platform, cancellationToken);

            // A download started meanwhile keeps the banner it has.
            if (IsDownloading)
            {
                return;
            }

            if (update is null)
            {
                if (announceResult)
                {
                    Status = $"Castorice {current} is the latest version.";
                }

                return;
            }

            // A skipped or dismissed version stays quiet on its own; asking in Settings still shows it.
            var version = update.Version.ToString();
            if (!announceResult &&
                (version == _services.Settings.SkippedUpdateVersion || version == _dismissedVersion))
            {
                return;
            }

            Offer(update);
        }
        catch (Exception ex) when (_disposed && ex is OperationCanceledException or ObjectDisposedException)
        {
            // Castorice is quitting.
        }
        catch (Exception ex)
        {
            // Anything else only costs this one check; the next one tries again.
            if (announceResult)
            {
                Status = $"Could not check for updates: {ex.Message}";
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void Offer(UpdateInfo update)
    {
        _update = update;
        BannerText = $"Castorice {update.Version} is available — you have {AppInfo.Version}.";
        Status = $"Castorice {update.Version} is available.";
        IsBannerVisible = true;
        OnPropertyChanged(nameof(InstallLabel));
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_update is not { } update || IsDownloading)
        {
            return;
        }

        if (!CanInstallInApp)
        {
            UpdateInstaller.OpenUrl(update.PageUrl);
            BannerText = $"The release page for {update.Version} is open in your browser.";
            return;
        }

        var asset = update.Asset!;
        var target = UpdateInstaller.DownloadPathFor(asset);
        var progress = new Progress<double>(value => DownloadProgress = value * 100);

        IsDownloading = true;
        DownloadProgress = 0;
        BannerText = $"Downloading Castorice {update.Version}…";

        try
        {
            await _checker.DownloadAsync(asset, target, progress, _lifetime.Token);

            var quit = UpdateInstaller.Apply(target, update, out var message);
            BannerText = message;

            if (quit)
            {
                // Gives the banner a moment to show what is happening before the window goes.
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.TryShutdown();
                }
            }
        }
        catch (Exception ex) when (_disposed && ex is OperationCanceledException or ObjectDisposedException)
        {
            // Castorice quit during the download; the next start offers the update again.
        }
        catch (Exception ex)
        {
            BannerText = $"The update failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        if (_update is { } update)
        {
            UpdateInstaller.OpenUrl(update.PageUrl);
        }
    }

    /// <summary>Stops offering this version; the next one is offered again.</summary>
    [RelayCommand]
    private void SkipVersion()
    {
        if (_update is { } update)
        {
            _services.Settings.SkippedUpdateVersion = update.Version.ToString();
            TrySaveSettings();
            Status = $"Skipped {update.Version}. Check now in Settings still offers it.";
        }

        IsBannerVisible = false;
    }

    /// <summary>Hides the banner until the next start.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        _dismissedVersion = _update?.Version.ToString();
        IsBannerVisible = false;
    }

    private void TrySaveSettings()
    {
        try
        {
            _services.SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save settings: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _checker.Dispose();
    }
}
