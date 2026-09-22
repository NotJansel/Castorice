using Castorice.Core.Bancho;
using Castorice.Core.Tournament;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Castorice.Desktop.ViewModels;

public sealed partial class RoomPlayerViewModel : ViewModelBase
{
    /// <summary>The slot "Move" sends the player to; starts at where they already are.</summary>
    [ObservableProperty]
    private int _targetSlot;

    public RoomPlayerViewModel(RoomPlayer player)
    {
        Model = player;
        TargetSlot = player.Slot > 0 ? player.Slot : 1;
    }

    public RoomPlayer Model { get; }

    public string Username => Model.Username;

    public int Slot => Model.Slot;

    public TeamColour? Team => Model.Team;

    public string TeamLabel => Model.Team?.ToString() ?? "-";

    public string Status => Model.Status.Length > 0 ? Model.Status : "In lobby";

    public string ModsDisplay => Model.Mods is Mods.None ? string.Empty : Model.Mods.ToCompactAcronyms();

    public string ScoreDisplay => Model.LastScore is { } score
        ? $"{score:N0}{(Model.LastScorePassed ? string.Empty : " (failed)")}"
        : "-";

    public string ProfileUrl => Model.UserId > 0 ? $"https://osu.ppy.sh/users/{Model.UserId}" : string.Empty;
}
