namespace Castorice.Core.Irc;

public enum IrcConnectionState
{
    Disconnected,
    Connecting,
    Registering,
    Connected,
    Reconnecting,
}

public sealed record IrcConnectionStateChanged(IrcConnectionState State, string? Detail = null);

/// <summary>Everything needed to reach Bancho's IRC gateway.</summary>
public sealed record IrcCredentials
{
    /// <summary>osu! username. Spaces are converted to underscores before being sent as the nick.</summary>
    public required string Username { get; init; }

    /// <summary>The IRC server password from <c>https://osu.ppy.sh/home/account/edit#legacy-api</c> — not the account password.</summary>
    public required string Password { get; init; }

    public string Host { get; init; } = "irc.ppy.sh";

    public int Port { get; init; } = 6667;

    public bool UseTls { get; init; }

    public string Nick => Username.Replace(' ', '_');
}
