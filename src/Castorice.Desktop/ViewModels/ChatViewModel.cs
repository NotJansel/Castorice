using System.Collections.ObjectModel;
using Castorice.Core.Chat;
using Castorice.Core.Irc;
using Castorice.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

public sealed partial class ChatViewModel : ViewModelBase
{
    private const int HistoryLimit = 100;

    private readonly AppServices _services;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    [ObservableProperty]
    private ChatTarget? _selectedTarget;

    [ObservableProperty]
    private string _draft = string.Empty;

    [ObservableProperty]
    private string _joinChannel = string.Empty;

    [ObservableProperty]
    private bool _showRawConsole;

    public ChatViewModel(AppServices services)
    {
        _services = services;

        _services.Chat.TargetOpened += (_, target) => SelectedTarget ??= target;
        _services.Irc.RawTraffic += OnRawTraffic;

        SelectedTarget = _services.Chat.Server;
    }

    public ReadOnlyObservableCollection<ChatTarget> Targets => _services.Chat.Targets;

    /// <summary>Wire traffic for the debug console, newest last.</summary>
    public ObservableCollection<string> RawLog { get; } = [];

    public bool IsConnected => _services.Irc.IsConnected;

    partial void OnSelectedTargetChanged(ChatTarget? value)
    {
        if (value is not null)
        {
            _services.Chat.SetActive(value);
        }

        SendCommand.NotifyCanExecuteChanged();
        CloseTargetCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Draft = string.Empty;
        RememberInHistory(text);

        try
        {
            var command = SlashCommand.Parse(text);
            if (command.Kind is SlashCommandKind.None)
            {
                await SendPlainTextAsync(SlashCommand.Unescape(text));
                return;
            }

            await RunSlashCommandAsync(command);
        }
        catch (IrcConnectionLostException ex)
        {
            // The connection can drop between the button enabling and the write going out.
            // Report it where the user is looking, and hand the text back rather than losing it.
            ReportNotSent(SelectedTarget, ex);

            if (Draft.Length == 0)
            {
                Draft = text;
            }
        }
    }

    private void ReportNotSent(ChatTarget? target, IrcConnectionLostException ex) =>
        _services.Chat.AppendClientNotice(
            target ?? _services.Chat.Server,
            $"Not sent — {ex.Message} Reconnect from the rail; your message is back in the box.");

    private bool CanSend() => _services.Irc.IsConnected && SelectedTarget is not null;

    private async Task SendPlainTextAsync(string text)
    {
        var target = SelectedTarget;
        if (target is null)
        {
            return;
        }

        if (target.Kind is ChatTargetKind.Server)
        {
            _services.Chat.AppendClientNotice(
                target,
                "This is the server log. Pick a channel or open a conversation to send messages.");
            return;
        }

        await _services.Chat.SendAsync(target, text);
    }

    private async Task RunSlashCommandAsync(SlashCommand command)
    {
        var target = SelectedTarget ?? _services.Chat.Server;

        switch (command.Kind)
        {
            case SlashCommandKind.Join:
                await JoinCoreAsync(command.Argument);
                break;

            case SlashCommandKind.Part:
                {
                    var channel = command.Argument.Length > 0 ? command.Argument : target.Name;
                    if (channel.StartsWith('#'))
                    {
                        await _services.Irc.PartAsync(channel);
                    }

                    _services.Chat.Close(channel);
                    break;
                }

            case SlashCommandKind.PrivateMessage:
                {
                    if (command.Argument.Length == 0)
                    {
                        _services.Chat.AppendClientNotice(target, "Usage: /msg <user> [message]");
                        break;
                    }

                    var conversation = _services.Chat.Open(command.Argument.Replace(' ', '_'));
                    SelectedTarget = conversation;

                    if (command.Remainder.Length > 0)
                    {
                        await _services.Chat.SendAsync(conversation, command.Remainder);
                    }

                    break;
                }

            case SlashCommandKind.Action:
                if (target.Kind is not ChatTargetKind.Server && command.Argument.Length > 0)
                {
                    await _services.Irc.SendMessageAsync(target.Name, $"\u0001ACTION {command.Argument}\u0001");
                    _services.Chat.AppendClientNotice(target, $"* {_services.Irc.CurrentNick} {command.Argument}");
                }

                break;

            case SlashCommandKind.Raw:
                if (command.Argument.Length > 0)
                {
                    await _services.Irc.SendRawAsync(command.Argument);
                }

                break;

            case SlashCommandKind.Quit:
                await _services.Irc.DisconnectAsync(command.Argument.Length > 0 ? command.Argument : "Castorice");
                break;

            default:
                _services.Chat.AppendClientNotice(target, $"Unknown command: /{command.Argument}");
                break;
        }
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        var channel = JoinChannel.Trim();
        if (channel.Length == 0)
        {
            return;
        }

        JoinChannel = string.Empty;
        await JoinCoreAsync(channel);
    }

    private async Task JoinCoreAsync(string channel)
    {
        if (channel.Length == 0)
        {
            return;
        }

        // A bare name is a channel; anything else opens a private conversation.
        if (!channel.StartsWith('#'))
        {
            channel = '#' + channel;
        }

        SelectedTarget = _services.Chat.Open(channel);

        if (!_services.Irc.IsConnected)
        {
            return;
        }

        try
        {
            await _services.Irc.JoinAsync(channel);
        }
        catch (IrcConnectionLostException ex)
        {
            _services.Chat.AppendClientNotice(
                SelectedTarget,
                $"Could not join {channel} — {ex.Message} It stays in the list; join again once reconnected.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCloseTarget))]
    private void CloseTarget()
    {
        var target = SelectedTarget;
        if (target is null)
        {
            return;
        }

        if (target.Kind is not ChatTargetKind.PrivateMessage && _services.Irc.IsConnected)
        {
            _ = PartQuietlyAsync(target.Name);
        }

        _services.Chat.Close(target.Name);
        SelectedTarget = _services.Chat.Server;
    }

    private bool CanCloseTarget() => SelectedTarget is { Kind: not ChatTargetKind.Server };

    /// <summary>
    /// Leaving a channel is best effort: the tab closes either way, and a dead connection has
    /// already left every channel on the server's side.
    /// </summary>
    private async Task PartQuietlyAsync(string channel)
    {
        try
        {
            await _services.Irc.PartAsync(channel);
        }
        catch (IrcConnectionLostException)
        {
        }
    }

    [RelayCommand]
    private void ClearRawLog() => RawLog.Clear();

    /// <summary>Recalls a previously sent line, the way an IRC client's up-arrow does.</summary>
    public void RecallHistory(int delta)
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex < 0)
        {
            _historyIndex = _history.Count;
        }

        _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count);
        Draft = _historyIndex >= _history.Count ? string.Empty : _history[_historyIndex];
    }

    /// <summary>Called when the connection state changes so the send button re-evaluates.</summary>
    public void NotifyConnectionChanged()
    {
        OnPropertyChanged(nameof(IsConnected));
        SendCommand.NotifyCanExecuteChanged();
    }

    private void RememberInHistory(string text)
    {
        if (_history.LastOrDefault() != text)
        {
            _history.Add(text);
            while (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
            }
        }

        _historyIndex = -1;
    }

    private void OnRawTraffic(object? sender, Core.Irc.Events.RawLine line)
    {
        if (!ShowRawConsole)
        {
            return;
        }

        AvaloniaDispatcher.Instance.Post(() =>
        {
            RawLog.Add($"{line.Timestamp:HH:mm:ss} {(line.Outbound ? "»" : "«")} {line.Line}");
            while (RawLog.Count > 500)
            {
                RawLog.RemoveAt(0);
            }
        });
    }
}
