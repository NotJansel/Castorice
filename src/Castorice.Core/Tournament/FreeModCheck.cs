using System.Text.Json.Serialization;

namespace Castorice.Core.Tournament;

/// <summary>
/// A class of mods a team has to field on a FreeMod pick, e.g. "at least one HardRock player".
/// A player counts towards the first group whose mods they carry, so the order matters:
/// HardRock is listed before Hidden/Easy, which puts an HDHR player in the HardRock group.
/// </summary>
public sealed class FreeModGroup
{
    public string Name { get; set; } = string.Empty;

    /// <summary>A player joins this group by carrying any one of these mods.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Mods>))]
    public Mods AnyOf { get; set; }

    public int MinimumPerTeam { get; set; } = 1;
}

/// <summary>A player carrying a mod the bracket does not permit at all.</summary>
public sealed record ForbiddenModEntry(string Username, Mods Mods)
{
    public string Describe() => $"{Username}: {Mods.ToCompactAcronyms()} not allowed";
}

/// <summary>A team short of the players it owes in one group.</summary>
public sealed record MissingGroupEntry(TeamColour Team, string GroupName, int Required, int Present)
{
    public int Shortfall => Required - Present;

    public string Describe() => $"{Team} needs {Shortfall}x {GroupName}";
}

public sealed record FreeModCheckResult(
    IReadOnlyList<ForbiddenModEntry> ForbiddenMods,
    IReadOnlyList<MissingGroupEntry> MissingGroups,
    int PlayersChecked)
{
    public bool IsClean => ForbiddenMods.Count == 0 && MissingGroups.Count == 0;

    /// <summary>False when nobody's mods are known yet, so the check could not say anything.</summary>
    public bool HasData => PlayersChecked > 0;

    /// <summary>One line for the lobby, naming every team and player that has to change something.</summary>
    public string Summary
    {
        get
        {
            if (IsClean)
            {
                return "FreeMod check: everyone is good.";
            }

            var parts = MissingGroups.Select(m => m.Describe())
                .Concat(ForbiddenMods.Select(f => f.Describe()));

            return "FreeMod check: " + string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Checks a lobby against a bracket's FreeMod rule.
/// <para>
/// The usual rule is not "everybody needs a mod" but a per-team quota: a team has to field one
/// player on a Hidden/Easy mod and one on HardRock, and whoever is left over may play NoMod. That
/// falls out of the quota on its own — a 3v3 team has one NoMod slot spare and a 4v4 team two —
/// so the team size never has to be configured.
/// </para>
/// </summary>
public static class FreeModCheck
{
    /// <summary>Mods a player may carry at all. Anything outside this is reported.</summary>
    public const Mods DefaultAllowed = Mods.Hidden | Mods.HardRock | Mods.Easy | Mods.Flashlight;

    /// <summary>
    /// The default quota: one HardRock and one Hidden/Easy player per team. HardRock is first so
    /// that an HDHR player fills the HardRock slot rather than the Hidden one.
    /// </summary>
    public static List<FreeModGroup> DefaultGroups() =>
    [
        new() { Name = "HR", AnyOf = Mods.HardRock, MinimumPerTeam = 1 },
        new() { Name = "HD/EZ", AnyOf = Mods.Hidden | Mods.Easy, MinimumPerTeam = 1 },
    ];

    public static FreeModCheckResult Check(
        IEnumerable<PlayerScoreInput> players,
        IReadOnlyList<FreeModGroup>? groups = null,
        Mods allowed = DefaultAllowed)
    {
        ArgumentNullException.ThrowIfNull(players);

        var roster = players.ToList();
        var quota = groups ?? DefaultGroups();

        var forbidden = new List<ForbiddenModEntry>();
        var counts = new Dictionary<(TeamColour Team, string Group), int>();

        foreach (var player in roster)
        {
            // NoFail is a safety net rather than a difficulty choice, and the room's own FreeMod
            // flag is not a player mod at all; neither counts for or against anything.
            var relevant = player.Mods & ~Mods.NoFail & ~Mods.FreeMod;

            var outside = relevant & ~allowed;
            if (outside is not Mods.None)
            {
                forbidden.Add(new ForbiddenModEntry(player.Username, outside));
                continue;
            }

            if (player.Team is not { } team)
            {
                continue;
            }

            // First match wins, which is what puts HDHR in the HardRock group.
            var group = quota.FirstOrDefault(g => (relevant & g.AnyOf) is not Mods.None);
            if (group is not null)
            {
                counts[(team, group.Name)] = counts.GetValueOrDefault((team, group.Name)) + 1;
            }
        }

        var missing = new List<MissingGroupEntry>();

        // Only judge a team that is actually present; an empty side is not a rule violation.
        foreach (var team in roster.Select(p => p.Team).OfType<TeamColour>().Distinct().Order())
        {
            foreach (var group in quota.Where(g => g.MinimumPerTeam > 0))
            {
                var present = counts.GetValueOrDefault((team, group.Name));
                if (present < group.MinimumPerTeam)
                {
                    missing.Add(new MissingGroupEntry(team, group.Name, group.MinimumPerTeam, present));
                }
            }
        }

        return new FreeModCheckResult(forbidden, missing, roster.Count);
    }
}
