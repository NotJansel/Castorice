using Castorice.Core.Tournament;

namespace Castorice.Desktop.ViewModels;

/// <summary>
/// One row of the FreeMod quota. The mod set is fixed in the pool file; what a referee actually
/// changes between brackets is how many players a team owes, so only that is editable here.
/// </summary>
public sealed class FreeModGroupViewModel(FreeModGroup model) : ViewModelBase
{
    public FreeModGroup Model { get; } = model;

    public string Name => Model.Name;

    /// <summary>"HD or EZ", not "EZHD": the group is a choice, not a combination.</summary>
    public string ModsDisplay => string.Join(" or ", Model.AnyOf.ToAcronymList());

    public int MinimumPerTeam
    {
        get => Model.MinimumPerTeam;
        set
        {
            if (Model.MinimumPerTeam == value)
            {
                return;
            }

            Model.MinimumPerTeam = value;
            OnPropertyChanged();
        }
    }
}
