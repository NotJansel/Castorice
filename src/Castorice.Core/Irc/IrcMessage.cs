namespace Castorice.Core.Irc;

/// <summary>
/// A single parsed line from the IRC stream, following the RFC 1459 grammar
/// <c>[":" prefix SPACE] command [params] [":" trailing]</c>.
/// </summary>
public sealed record IrcMessage
{
    /// <summary>The raw line as it arrived, without the trailing CRLF.</summary>
    public required string Raw { get; init; }

    /// <summary>The prefix without the leading colon, e.g. <c>Peppy!cho@ppy.sh</c>. Empty when the line had none.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>The command or three digit numeric, upper-cased, e.g. <c>PRIVMSG</c> or <c>001</c>.</summary>
    public required string Command { get; init; }

    /// <summary>Middle parameters plus the trailing parameter as the last element.</summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];

    /// <summary>The nickname part of <see cref="Prefix"/>, or the whole prefix when it carries no user/host.</summary>
    public string Nick
    {
        get
        {
            if (Prefix.Length == 0)
            {
                return string.Empty;
            }

            var bang = Prefix.IndexOf('!');
            if (bang >= 0)
            {
                return Prefix[..bang];
            }

            var at = Prefix.IndexOf('@');
            return at >= 0 ? Prefix[..at] : Prefix;
        }
    }

    /// <summary>The trailing parameter, i.e. the message body of a PRIVMSG.</summary>
    public string Trailing => Parameters.Count > 0 ? Parameters[^1] : string.Empty;

    public string ParameterAt(int index) =>
        index >= 0 && index < Parameters.Count ? Parameters[index] : string.Empty;

    /// <summary>
    /// Parses one wire line. Returns <c>null</c> for blank lines; every other input yields a
    /// message so that unknown commands stay visible to the caller instead of being dropped.
    /// </summary>
    public static IrcMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var raw = line.TrimEnd('\r', '\n');
        var rest = raw.AsSpan();
        var prefix = string.Empty;

        if (rest.Length > 0 && rest[0] == ':')
        {
            var space = rest.IndexOf(' ');
            if (space < 0)
            {
                // A prefix and nothing else is not a usable message.
                return new IrcMessage { Raw = raw, Prefix = rest[1..].ToString(), Command = string.Empty };
            }

            prefix = rest[1..space].ToString();
            rest = rest[(space + 1)..].TrimStart(' ');
        }

        var commandEnd = rest.IndexOf(' ');
        string command;
        if (commandEnd < 0)
        {
            command = rest.ToString();
            rest = [];
        }
        else
        {
            command = rest[..commandEnd].ToString();
            rest = rest[(commandEnd + 1)..];
        }

        var parameters = new List<string>();
        while (rest.Length > 0)
        {
            rest = rest.TrimStart(' ');
            if (rest.Length == 0)
            {
                break;
            }

            if (rest[0] == ':')
            {
                parameters.Add(rest[1..].ToString());
                break;
            }

            var space = rest.IndexOf(' ');
            if (space < 0)
            {
                parameters.Add(rest.ToString());
                break;
            }

            parameters.Add(rest[..space].ToString());
            rest = rest[(space + 1)..];
        }

        return new IrcMessage
        {
            Raw = raw,
            Prefix = prefix,
            Command = command.ToUpperInvariant(),
            Parameters = parameters,
        };
    }
}
