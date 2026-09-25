using System.Text.Json.Serialization;
using Castorice.Core.Tournament;

namespace Castorice.Core.Configuration;

public sealed class AppSettings
{
    /// <summary>osu! username used for both IRC and the "my profile" shortcut.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>IRC server password from the legacy API page. Stored locally in plain text.</summary>
    public string IrcPassword { get; set; } = string.Empty;

    public string IrcHost { get; set; } = "irc.ppy.sh";

    public int IrcPort { get; set; } = 6667;

    public bool UseTls { get; set; }

    public bool AutoConnect { get; set; }

    /// <summary>Channels joined right after registration.</summary>
    public List<string> AutoJoinChannels { get; set; } = ["#osu"];

    /// <summary>Set when osu! staff have flagged the account as a bot, which raises the send quota.</summary>
    public bool HasBotAccount { get; set; }

    /// <summary>OAuth client id from <c>https://osu.ppy.sh/home/account/edit#oauth</c>, for the profile view.</summary>
    public string OsuClientId { get; set; } = string.Empty;

    public string OsuClientSecret { get; set; } = string.Empty;

    /// <summary>File name (not path) of the mappool selected in the tournament panel.</summary>
    public string? LastMappoolFile { get; set; }

    public string Theme { get; set; } = "Dark";

    /// <summary>Ask before sending destructive commands such as <c>!mp close</c>.</summary>
    public bool ConfirmDestructiveCommands { get; set; } = true;

    /// <summary>What the tournament panel posts into the lobby without being asked.</summary>
    public LobbyAnnouncements Announcements { get; set; } = new();

    [JsonIgnore]
    public bool HasIrcCredentials =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(IrcPassword);

    [JsonIgnore]
    public bool HasApiCredentials =>
        !string.IsNullOrWhiteSpace(OsuClientId) && !string.IsNullOrWhiteSpace(OsuClientSecret);
}
