using System.Collections.ObjectModel;

namespace Castorice.Core.Chat;

public enum ChatTargetKind
{
    Channel,
    PrivateMessage,

    /// <summary>A <c>#mp_*</c> channel created by <c>!mp make</c>.</summary>
    MultiplayerRoom,

    /// <summary>The pseudo-target that collects server notices before any channel is joined.</summary>
    Server,
}

/// <summary>A channel or conversation, with its backlog and unread bookkeeping.</summary>
public sealed class ChatTarget
{
    public const string BanchoBot = "BanchoBot";
    public const int BacklogLimit = 2000;

    private readonly ObservableCollection<ChatMessage> _messages = [];

    public ChatTarget(string name, ChatTargetKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        Messages = new ReadOnlyObservableCollection<ChatMessage>(_messages);
    }

    public string Name { get; }

    public ChatTargetKind Kind { get; }

    public ReadOnlyObservableCollection<ChatMessage> Messages { get; }

    public int UnreadCount { get; private set; }

    public bool HasUnreadHighlight { get; private set; }

    /// <summary>Users currently in the channel, as reported by the NAMES reply and JOIN/PART.</summary>
    public SortedSet<string> Users { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ChatTargetKind KindFor(string name) => name switch
    {
        _ when name.StartsWith("#mp_", StringComparison.OrdinalIgnoreCase) => ChatTargetKind.MultiplayerRoom,
        _ when name.StartsWith('#') => ChatTargetKind.Channel,
        _ => ChatTargetKind.PrivateMessage,
    };

    public void Append(ChatMessage message, bool isActive)
    {
        _messages.Add(message);
        while (_messages.Count > BacklogLimit)
        {
            _messages.RemoveAt(0);
        }

        if (isActive || message.Kind is ChatMessageKind.Outgoing)
        {
            return;
        }

        UnreadCount++;
        HasUnreadHighlight |= message.IsHighlight;
    }

    public void MarkRead()
    {
        UnreadCount = 0;
        HasUnreadHighlight = false;
    }
}
