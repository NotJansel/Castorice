using Castorice.Core.Chat;
using Castorice.Core.Configuration;
using Castorice.Core.Irc;
using Castorice.Core.MappoolBuilder;
using Castorice.Core.Osu;
using Castorice.Core.Tournament;

namespace Castorice.Desktop.Services;

/// <summary>
/// The object graph shared by every page. Small enough that a container would be ceremony.
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    public AppServices(SettingsStore? settingsStore = null)
    {
        SettingsStore = settingsStore ?? new SettingsStore();
        Settings = SettingsStore.Load();

        Irc = new IrcClient(Settings.HasBotAccount
            ? OutboundRateLimiter.ForBotAccount()
            : OutboundRateLimiter.ForPlayerAccount());

        Chat = new ChatService(Irc, AvaloniaDispatcher.Instance);
        Tournament = new TournamentController(Irc, Chat, AvaloniaDispatcher.Instance);
        Mappools = new MappoolStore();
        Api = new OsuApiClient();
        PoolBuilder = new MappoolBuilderClient();

        if (Settings.HasApiCredentials)
        {
            Api.Configure(Settings.OsuClientId, Settings.OsuClientSecret);
        }
    }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public IrcClient Irc { get; }

    public ChatService Chat { get; }

    public TournamentController Tournament { get; }

    public MappoolStore Mappools { get; }

    public OsuApiClient Api { get; }

    public MappoolBuilderClient PoolBuilder { get; }

    /// <summary>Read fresh on every call, so a changed address or token applies straight away.</summary>
    public MappoolBuilderConnection PoolBuilderConnection =>
        new(Settings.MappoolBuilderUrl, Settings.MappoolBuilderToken);

    public void SaveSettings() => SettingsStore.Save(Settings);

    public async ValueTask DisposeAsync()
    {
        await Irc.DisposeAsync();
        Api.Dispose();
        PoolBuilder.Dispose();
    }
}
