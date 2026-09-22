namespace Castorice.Core.Tournament;

public enum FreeModProblem
{
    /// <summary>The player carries none of the mods the bracket requires on a FreeMod pick.</summary>
    NoRequiredMod,

    /// <summary>The player carries a mod the bracket does not permit.</summary>
    ForbiddenMod,
}

public sealed record FreeModViolation(string Username, FreeModProblem Problem, Mods Mods)
{
    public string Describe() => Problem switch
    {
        FreeModProblem.NoRequiredMod => $"{Username}: no mod",
        _ => $"{Username}: {Mods.ToCompactAcronyms()} not allowed",
    };
}

public sealed record FreeModCheckResult(IReadOnlyList<FreeModViolation> Violations, int PlayersChecked)
{
    public bool IsClean => Violations.Count == 0;

    /// <summary>False when nobody's mods are known yet, i.e. the check could not say anything.</summary>
    public bool HasData => PlayersChecked > 0;

    /// <summary>One line for the lobby, naming everyone who has to change something.</summary>
    public string Summary => IsClean
        ? "FreeMod check: everyone is good."
        : "FreeMod check: " + string.Join(" · ", Violations.Select(v => v.Describe()));
}

/// <summary>
/// Checks a lobby against a bracket's FreeMod rule: on a FreeMod pick every player normally has to
/// take at least one mod from an allowed set, and nothing outside it.
/// </summary>
public static class FreeModCheck
{
    /// <summary>
    /// The mods brackets usually permit on a FreeMod pick. NoFail is tolerated on top of these
    /// rather than counting towards the requirement, since it is not a difficulty choice.
    /// </summary>
    public const Mods DefaultAllowed = Mods.Hidden | Mods.HardRock | Mods.Easy | Mods.Flashlight;

    public static FreeModCheckResult Check(
        IEnumerable<PlayerScoreInput> players,
        Mods allowed = DefaultAllowed,
        bool requireAtLeastOne = true)
    {
        ArgumentNullException.ThrowIfNull(players);

        var violations = new List<FreeModViolation>();
        var checkedCount = 0;

        foreach (var player in players)
        {
            checkedCount++;

            // NoFail never counts for or against the requirement.
            var relevant = player.Mods & ~Mods.NoFail & ~Mods.FreeMod;

            var forbidden = relevant & ~allowed;
            if (forbidden is not Mods.None)
            {
                violations.Add(new FreeModViolation(player.Username, FreeModProblem.ForbiddenMod, forbidden));
                continue;
            }

            if (requireAtLeastOne && relevant is Mods.None)
            {
                violations.Add(new FreeModViolation(player.Username, FreeModProblem.NoRequiredMod, Mods.None));
            }
        }

        return new FreeModCheckResult(violations, checkedCount);
    }
}
