namespace Castorice.Core.Chat;

public enum ChatMessageKind
{
    /// <summary>A normal message from another user.</summary>
    Message,

    /// <summary>A message this client sent.</summary>
    Outgoing,

    /// <summary>A <c>NOTICE</c>, or server text such as the MOTD.</summary>
    Notice,

    /// <summary>A join/part/quit or other channel bookkeeping line.</summary>
    System,

    /// <summary>A line the client itself produced, e.g. an error explaining a failed command.</summary>
    Client,

    /// <summary>A <c>/me</c> action.</summary>
    Action,
}

public sealed record ChatMessage
{
    public required string Sender { get; init; }

    public required string Text { get; init; }

    public ChatMessageKind Kind { get; init; } = ChatMessageKind.Message;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>True when the message mentions the local user and should be highlighted.</summary>
    public bool IsHighlight { get; init; }

    public bool IsFromBanchoBot => string.Equals(Sender, ChatTarget.BanchoBot, StringComparison.OrdinalIgnoreCase);
}
