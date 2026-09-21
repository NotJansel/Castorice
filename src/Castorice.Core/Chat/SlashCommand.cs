namespace Castorice.Core.Chat;

public enum SlashCommandKind
{
    /// <summary>Not a command — send the text as-is.</summary>
    None,
    Join,
    Part,
    PrivateMessage,
    Action,
    Quit,
    /// <summary>Send the rest of the line straight to the server.</summary>
    Raw,
    Unknown,
}

public sealed record SlashCommand(SlashCommandKind Kind, string Argument, string Remainder)
{
    public static readonly SlashCommand NotACommand = new(SlashCommandKind.None, string.Empty, string.Empty);

    /// <summary>
    /// Splits the leading <c>/word</c> off a chat input. A doubled slash (<c>//</c>) escapes into
    /// literal text, which is how you say "/mp start" in chat without running it.
    /// </summary>
    public static SlashCommand Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input[0] is not '/' || input.StartsWith("//", StringComparison.Ordinal))
        {
            return NotACommand;
        }

        var body = input[1..];
        var space = body.IndexOf(' ');
        var verb = (space < 0 ? body : body[..space]).ToLowerInvariant();
        var rest = space < 0 ? string.Empty : body[(space + 1)..].Trim();

        var argumentEnd = rest.IndexOf(' ');
        var argument = argumentEnd < 0 ? rest : rest[..argumentEnd];
        var remainder = argumentEnd < 0 ? string.Empty : rest[(argumentEnd + 1)..].Trim();

        return verb switch
        {
            "join" or "j" => new SlashCommand(SlashCommandKind.Join, argument, remainder),
            "part" or "leave" or "close" => new SlashCommand(SlashCommandKind.Part, argument, remainder),
            "msg" or "query" or "pm" => new SlashCommand(SlashCommandKind.PrivateMessage, argument, remainder),
            "me" => new SlashCommand(SlashCommandKind.Action, rest, string.Empty),
            "quit" or "disconnect" => new SlashCommand(SlashCommandKind.Quit, rest, string.Empty),
            "raw" or "quote" => new SlashCommand(SlashCommandKind.Raw, rest, string.Empty),
            _ => new SlashCommand(SlashCommandKind.Unknown, verb, rest),
        };
    }

    /// <summary>Strips the escaping slash from <c>//text</c>.</summary>
    public static string Unescape(string input) =>
        input.StartsWith("//", StringComparison.Ordinal) ? input[1..] : input;
}
