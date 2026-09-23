using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

/// <summary>
/// A channel or conversation, with its backlog and unread bookkeeping. It raises change
/// notifications because the conversation list and the header bind straight to it; before it did,
/// the unread badge and user count showed whatever they were when first drawn, and never moved.
/// </summary>
public sealed class ChatTarget : INotifyPropertyChanged
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

    private readonly SortedSet<string> _users = new(StringComparer.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    public int UnreadCount
    {
        get;
        private set => SetField(ref field, value);
    }

    public bool HasUnreadHighlight
    {
        get;
        private set => SetField(ref field, value);
    }

    /// <summary>
    /// Users currently in the channel, as reported by the NAMES reply and JOIN/PART. Read-only from
    /// outside, so every change goes through the methods below and <see cref="UserCount"/> follows.
    /// </summary>
    public IReadOnlyCollection<string> Users => _users;

    public int UserCount => _users.Count;

    public void AddUser(string nick)
    {
        if (_users.Add(nick))
        {
            OnPropertyChanged(nameof(UserCount));
        }
    }

    public void RemoveUser(string nick)
    {
        if (_users.Remove(nick))
        {
            OnPropertyChanged(nameof(UserCount));
        }
    }

    /// <summary>Forgets the member list, e.g. on joining, before the server sends the whole list again.</summary>
    public void ClearUsers()
    {
        if (_users.Count == 0)
        {
            return;
        }

        _users.Clear();
        OnPropertyChanged(nameof(UserCount));
    }

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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
