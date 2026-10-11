using Castorice.Core.Bancho;
using Castorice.Core.Chat;
using Castorice.Core.Irc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

/// <summary>The attached lobby's chat, shown and written to from the tournament page.</summary>
public sealed partial class TournamentViewModel
{
    /// <summary>The <c>#mp_</c> channel of the attached lobby; the same conversation the Chat page shows.</summary>
    [ObservableProperty]
    private ChatTarget? _lobbyChat;

    [ObservableProperty]
    private string _lobbyChatDraft = string.Empty;

    public bool HasLobbyChat => LobbyChat is not null;

    partial void OnLobbyChatChanged(ChatTarget? value)
    {
        OnPropertyChanged(nameof(HasLobbyChat));
        SendLobbyChatCommand.NotifyCanExecuteChanged();
    }

    private void ShowLobbyChat(MultiplayerRoom? room) =>
        LobbyChat = room is null ? null : _services.Chat.Open(room.ChannelName);

    private bool CanSendLobbyChat() => LobbyChat is not null;

    /// <summary>
    /// Sends the line to the lobby as typed, so <c>!mp</c> commands work here as well. The Chat page's
    /// slash commands stay there.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendLobbyChat))]
    private async Task SendLobbyChatAsync()
    {
        var text = LobbyChatDraft.Trim();
        if (text.Length == 0 || LobbyChat is not { } target)
        {
            return;
        }

        LobbyChatDraft = string.Empty;

        try
        {
            await _services.Chat.SendAsync(target, text);
        }
        catch (IrcConnectionLostException ex)
        {
            Status = $"Not sent — {ex.Message}";
            if (LobbyChatDraft.Length == 0)
            {
                LobbyChatDraft = text;
            }
        }
    }
}
