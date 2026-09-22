using System.Collections.ObjectModel;
using Castorice.Core.Irc;

namespace Castorice.Core.Chat;

/// <summary>
/// Turns the raw IRC stream into per-target conversation backlogs, and routes outgoing text back
/// to the client. This is the single source of truth the chat UI binds to.
/// </summary>
public sealed class ChatService
{
    private readonly IrcClient _client;
    private readonly IUiDispatcher _dispatcher;
    private readonly ObservableCollection<ChatTarget> _targets = [];
    private readonly Dictionary<string, ChatTarget> _byName = new(StringComparer.OrdinalIgnoreCase);

    public ChatService(IrcClient client, IUiDispatcher? dispatcher = null)
    {
        _client = client;
        _dispatcher = dispatcher ?? IUiDispatcher.Immediate;
        Targets = new ReadOnlyObservableCollection<ChatTarget>(_targets);

        Server = GetOrCreate("Server", ChatTargetKind.Server);
        ActiveTarget = Server;

        _client.MessageReceived += OnMessageReceived;
    }

    public ReadOnlyObservableCollection<ChatTarget> Targets { get; }

    /// <summary>The catch-all target for server notices and the MOTD.</summary>
    public ChatTarget Server { get; }

    /// <summary>The conversation currently shown; messages arriving here do not raise the unread count.</summary>
    public ChatTarget ActiveTarget { get; private set; }

    public event EventHandler<ChatMessage>? MessageAppended;

    public event EventHandler<ChatTarget>? TargetOpened;

    public void SetActive(ChatTarget target)
    {
        ActiveTarget = target;
        target.MarkRead();
    }

    public ChatTarget Open(string name)
    {
        var target = GetOrCreate(name, ChatTarget.KindFor(name));
        return target;
    }

    public ChatTarget? Find(string name) => _byName.GetValueOrDefault(name);

    public void Close(string name)
    {
        if (!_byName.TryGetValue(name, out var target) || target.Kind is ChatTargetKind.Server)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            _byName.Remove(name);
            _targets.Remove(target);
            if (ReferenceEquals(ActiveTarget, target))
            {
                ActiveTarget = Server;
            }
        });
    }

    /// <summary>Sends text to a target and echoes it into the backlog, since Bancho does not echo back.</summary>
    public async Task SendAsync(ChatTarget target, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || target.Kind is ChatTargetKind.Server)
        {
            return;
        }

        await _client.SendMessageAsync(target.Name, text, cancellationToken).ConfigureAwait(false);

        foreach (var line in IrcClient.SplitForWire(text))
        {
            Append(target, new ChatMessage
            {
                Sender = _client.CurrentNick,
                Text = line,
                Kind = ChatMessageKind.Outgoing,
            });
        }
    }

    public void AppendClientNotice(ChatTarget target, string text) =>
        Append(target, new ChatMessage { Sender = "castorice", Text = text, Kind = ChatMessageKind.Client });

    private void OnMessageReceived(object? sender, IrcMessage message)
    {
        switch (message.Command)
        {
            case "PRIVMSG":
                HandlePrivmsg(message);
                break;

            case "NOTICE":
                Append(Server, new ChatMessage
                {
                    Sender = message.Nick.Length > 0 ? message.Nick : "server",
                    Text = message.Trailing,
                    Kind = ChatMessageKind.Notice,
                });
                break;

            case "JOIN":
                HandleMembership(message, joined: true);
                break;

            case "PART":
                HandleMembership(message, joined: false);
                break;

            case "QUIT":
                HandleQuit(message.Nick);
                break;

            case IrcNumerics.NamesReply:
                HandleNames(message);
                break;

            case IrcNumerics.Topic:
                if (Find(message.ParameterAt(1)) is { } topicTarget)
                {
                    Append(topicTarget, new ChatMessage
                    {
                        Sender = "topic",
                        Text = message.Trailing,
                        Kind = ChatMessageKind.System,
                    });
                }

                break;

            case IrcNumerics.Motd:
            case IrcNumerics.MotdStart:
            case IrcNumerics.MotdEnd:
            case IrcNumerics.Welcome:
                Append(Server, new ChatMessage
                {
                    Sender = "server",
                    Text = message.Trailing,
                    Kind = ChatMessageKind.Notice,
                });
                break;

            case IrcNumerics.NoSuchNick:
                Append(Server, new ChatMessage
                {
                    Sender = "server",
                    Text = $"{message.ParameterAt(1)}: {message.Trailing}",
                    Kind = ChatMessageKind.Notice,
                });
                break;
        }
    }

    private void HandlePrivmsg(IrcMessage message)
    {
        var destination = message.ParameterAt(0);
        var isChannel = destination.StartsWith('#');

        // A private message is filed under the sender, not under our own nick.
        var targetName = isChannel ? destination : message.Nick;
        if (targetName.Length == 0)
        {
            return;
        }

        var target = GetOrCreate(targetName, ChatTarget.KindFor(targetName));
        var text = message.Trailing;
        var kind = ChatMessageKind.Message;

        // CTCP ACTION, i.e. /me.
        if (text.Length > 8 && text[0] == '\u0001' && text.StartsWith("\u0001ACTION ", StringComparison.Ordinal))
        {
            text = text[8..].TrimEnd('\u0001');
            kind = ChatMessageKind.Action;
        }

        Append(target, new ChatMessage
        {
            Sender = message.Nick,
            Text = text,
            Kind = kind,
            IsHighlight = MentionsMe(text),
        });
    }

    private void HandleMembership(IrcMessage message, bool joined)
    {
        var channel = message.ParameterAt(0);
        if (channel.Length == 0)
        {
            channel = message.Trailing;
        }

        if (channel.Length == 0)
        {
            return;
        }

        var isSelf = string.Equals(message.Nick, _client.CurrentNick, StringComparison.OrdinalIgnoreCase);
        if (!joined && isSelf)
        {
            Close(channel);
            return;
        }

        var target = GetOrCreate(channel, ChatTarget.KindFor(channel));

        _dispatcher.Post(() =>
        {
            if (joined)
            {
                target.Users.Add(message.Nick);
            }
            else
            {
                target.Users.Remove(message.Nick);
            }
        });

        // Joins and parts deliberately produce no chat line. In a busy osu! channel they drown out
        // the conversation, and the user list above the backlog already shows who is present.
    }

    private void HandleQuit(string nick)
    {
        if (nick.Length == 0)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            foreach (var target in _targets)
            {
                target.Users.Remove(nick);
            }
        });
    }

    private void HandleNames(IrcMessage message)
    {
        // 353 <nick> <symbol> <channel> :<names>
        var channel = message.Parameters.FirstOrDefault(p => p.StartsWith('#'));
        if (channel is null)
        {
            return;
        }

        var target = GetOrCreate(channel, ChatTarget.KindFor(channel));
        var names = message.Trailing.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        _dispatcher.Post(() =>
        {
            foreach (var name in names)
            {
                target.Users.Add(name.TrimStart('@', '+', '%', '&', '~'));
            }
        });
    }

    private bool MentionsMe(string text)
    {
        var nick = _client.CurrentNick;
        if (nick.Length == 0)
        {
            return false;
        }

        return text.Contains(nick, StringComparison.OrdinalIgnoreCase)
            || text.Contains(nick.Replace('_', ' '), StringComparison.OrdinalIgnoreCase);
    }

    private ChatTarget GetOrCreate(string name, ChatTargetKind kind)
    {
        if (_byName.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var target = new ChatTarget(name, kind);
        _byName[name] = target;
        _dispatcher.Post(() =>
        {
            _targets.Add(target);
            TargetOpened?.Invoke(this, target);
        });

        return target;
    }

    private void Append(ChatTarget target, ChatMessage message)
    {
        _dispatcher.Post(() =>
        {
            target.Append(message, ReferenceEquals(target, ActiveTarget));
            MessageAppended?.Invoke(this, message);
        });
    }
}
