using System.Runtime.CompilerServices;
using Castorice.Core.Tournament;

namespace Castorice.Desktop.ViewModels;

/// <summary>
/// The switches for what the tournament panel posts into the lobby by itself. Each change is
/// saved straight away, so the choice carries over to the next match.
/// </summary>
public sealed class LobbyMessagesViewModel(LobbyAnnouncements model, Action save) : ViewModelBase
{
    public LobbyAnnouncements Model { get; } = model;

    public bool MapResult
    {
        get => Model.MapResult;
        set => Set(Model.MapResult, value, v => Model.MapResult = v);
    }

    public bool Multipliers
    {
        get => Model.Multipliers;
        set => Set(Model.Multipliers, value, v => Model.Multipliers = v);
    }

    public bool MatchScore
    {
        get => Model.MatchScore;
        set => Set(Model.MatchScore, value, v => Model.MatchScore = v);
    }

    public bool ScoreOnManualPoint
    {
        get => Model.ScoreOnManualPoint;
        set => Set(Model.ScoreOnManualPoint, value, v => Model.ScoreOnManualPoint = v);
    }

    public bool DraftActions
    {
        get => Model.DraftActions;
        set => Set(Model.DraftActions, value, v => Model.DraftActions = v);
    }

    public bool NextTurn
    {
        get => Model.NextTurn;
        set => Set(Model.NextTurn, value, v => Model.NextTurn = v);
    }

    public bool FreeModWarnings
    {
        get => Model.FreeModWarnings;
        set => Set(Model.FreeModWarnings, value, v => Model.FreeModWarnings = v);
    }

    private void Set(bool current, bool value, Action<bool> assign, [CallerMemberName] string? name = null)
    {
        if (current == value)
        {
            return;
        }

        assign(value);
        OnPropertyChanged(name);
        save();
    }
}
