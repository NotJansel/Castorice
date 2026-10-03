using Avalonia.Styling;
using Castorice.Core.Chat;
using Castorice.Core.Configuration;
using Castorice.Core.Irc;
using Castorice.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

public enum AppPage
{
    Chat,
    Tournament,
    Profile,
    Settings,
}

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    [ObservableProperty]
    private AppPage _currentPage = AppPage.Chat;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _connectionError;

    public MainWindowViewModel(AppServices? services = null)
    {
        _services = services ?? new AppServices();

        Chat = new ChatViewModel(_services);
        Tournament = new TournamentViewModel(_services);
        Profile = new ProfileViewModel(_services);
        Settings = new SettingsViewModel(_services);
        Updates = new UpdateViewModel(_services);
        Settings.Updates = Updates;
        Settings.Messages = Tournament.Messages;
        Updates.Start();

        Settings.ThemeChanged += (_, variant) => ThemeChanged?.Invoke(this, variant);
        Settings.ApiCredentialsChanged += (_, _) => Profile.NotifyApiConfigurationChanged();

        _services.Irc.StateChanged += OnIrcStateChanged;

        if (_services.Settings.AutoConnect && _services.Settings.HasIrcCredentials)
        {
            _ = ConnectAsync();
        }
    }

    public ChatViewModel Chat { get; }

    public UpdateViewModel Updates { get; }

    public TournamentViewModel Tournament { get; }

    public ProfileViewModel Profile { get; }

    public SettingsViewModel Settings { get; }

    public AppSettings AppSettings => _services.Settings;

    public bool IsConnected => _services.Irc.IsConnected;

    public string WindowTitle => _services.Settings.Username is { Length: > 0 } name
        ? $"Castorice — {name}"
        : "Castorice";

    public bool IsChatPage => CurrentPage is AppPage.Chat;

    public bool IsTournamentPage => CurrentPage is AppPage.Tournament;

    public bool IsProfilePage => CurrentPage is AppPage.Profile;

    public bool IsSettingsPage => CurrentPage is AppPage.Settings;

    public event EventHandler<ThemeVariant>? ThemeChanged;

    [RelayCommand]
    private void GoTo(AppPage page) => CurrentPage = page;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (!_services.Settings.HasIrcCredentials)
        {
            ConnectionError = "Enter your osu! username and IRC password in Settings first.";
            CurrentPage = AppPage.Settings;
            return;
        }

        IsConnecting = true;
        ConnectionError = null;
        ConnectionStatus = "Connecting…";

        try
        {
            await _services.Irc.ConnectAsync(new IrcCredentials
            {
                Username = _services.Settings.Username,
                Password = _services.Settings.IrcPassword,
                Host = _services.Settings.IrcHost,
                Port = _services.Settings.IrcPort,
                UseTls = _services.Settings.UseTls,
            });

            // On a reconnect this also brings back every channel that was open before the drop,
            // the attached lobby included; the server let go of all of them when the link died.
            var lobby = _services.Tournament.Room;
            var channels = ReconnectPlan.ChannelsToJoin(
                _services.Settings.AutoJoinChannels,
                _services.Chat.Targets,
                lobby?.ChannelName);

            foreach (var channel in channels)
            {
                _services.Chat.Open(channel);
                await _services.Irc.JoinAsync(channel);
            }

            // BanchoBot is where !mp make is answered, so keep that conversation open from the start.
            _services.Chat.Open(ChatTarget.BanchoBot);

            if (lobby is not null)
            {
                // Players may have joined, left or changed mods while we were away.
                await _services.Tournament.RefreshSettingsAsync();
                _services.Chat.AppendClientNotice(
                    _services.Chat.Open(lobby.ChannelName),
                    "Reconnected — rejoined the lobby and asked BanchoBot for its current settings.");
            }
        }
        catch (IrcAuthenticationException ex)
        {
            ConnectionError = ex.Message;
            ConnectionStatus = "Login rejected";
        }
        catch (Exception ex)
        {
            ConnectionError = ex.Message;
            ConnectionStatus = "Connection failed";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnect() => !IsConnecting && !_services.Irc.IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await _services.Irc.DisconnectAsync("Castorice");
        ConnectionStatus = "Disconnected";
    }

    private bool CanDisconnect() => _services.Irc.IsConnected;

    public async Task ShutdownAsync()
    {
        Updates.Dispose();

        try
        {
            _services.SaveSettings();
        }
        catch (Exception)
        {
            // Settings that cannot be written must not block shutdown.
        }

        await _services.DisposeAsync();
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        if (value is AppPage.Settings)
        {
            Settings.RefreshImageCacheSummary();
        }

        OnPropertyChanged(nameof(IsChatPage));
        OnPropertyChanged(nameof(IsTournamentPage));
        OnPropertyChanged(nameof(IsProfilePage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    private void OnIrcStateChanged(object? sender, IrcConnectionStateChanged e)
    {
        AvaloniaDispatcher.Instance.Post(() =>
        {
            ConnectionStatus = e.State switch
            {
                IrcConnectionState.Connected => $"Connected as {_services.Irc.CurrentNick}",
                IrcConnectionState.Connecting => "Connecting…",
                IrcConnectionState.Registering => "Registering…",
                IrcConnectionState.Reconnecting => "Reconnecting…",
                _ => e.Detail is { Length: > 0 } detail ? $"Disconnected — {detail}" : "Disconnected",
            };

            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(WindowTitle));

            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            Chat.NotifyConnectionChanged();
        });
    }
}
