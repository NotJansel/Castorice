using Castorice.Core.Tournament;

namespace Castorice.Desktop.ViewModels;

/// <summary>
/// One row of the FreeMod quota. The mod set is fixed in the pool file; what a referee actually
/// changes between brackets is how many players a team owes, so only that is editable here.
/// </summary>
public sealed class FreeModGroupViewModel(FreeModGroup model, Mods claimedByEarlierGroups) : ViewModelBase
{
    public FreeModGroup Model { get; } = model;

    public string Name => Model.Name;

    /// <summary>
    /// What fills this slot. A combination counts for the first group it matches, so a later group
    /// spells out what it excludes: without that, "HR" alone does not say that HDHR fills it and
    /// HD/EZ does not.
    /// </summary>
    public string ModsDisplay
    {
        get
        {
            var own = string.Join(" or ", Model.AnyOf.ToAcronymList());

            return claimedByEarlierGroups is Mods.None
                ? own
                : $"{own} — but not with {string.Join(" or ", claimedByEarlierGroups.ToAcronymList())}";
        }
    }

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
