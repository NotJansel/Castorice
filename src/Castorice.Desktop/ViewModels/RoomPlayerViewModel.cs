using Castorice.Core.Bancho;
using Castorice.Core.Tournament;

namespace Castorice.Desktop.ViewModels;

public sealed class RoomPlayerViewModel(RoomPlayer player) : ViewModelBase
{
    public RoomPlayer Model { get; } = player;

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
