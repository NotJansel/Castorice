namespace Castorice.Core.Tournament;

/// <summary>The mods Bancho accepts in <c>!mp mods</c>.</summary>
[Flags]
public enum Mods
{
    None = 0,
    NoFail = 1 << 0,
    Easy = 1 << 1,
    Hidden = 1 << 2,
    HardRock = 1 << 3,
    SuddenDeath = 1 << 4,
    DoubleTime = 1 << 5,
    Relax = 1 << 6,
    HalfTime = 1 << 7,
    Nightcore = 1 << 8,
    Flashlight = 1 << 9,
    SpunOut = 1 << 10,
    Perfect = 1 << 11,

    /// <summary>Not a mod: tells the room that players pick their own.</summary>
    FreeMod = 1 << 12,
}

public static class ModsExtensions
{
    private static readonly (Mods Mod, string Acronym)[] Table =
    [
        (Mods.NoFail, "NF"),
        (Mods.Easy, "EZ"),
        (Mods.Hidden, "HD"),
        (Mods.HardRock, "HR"),
        (Mods.SuddenDeath, "SD"),
        (Mods.DoubleTime, "DT"),
        (Mods.Relax, "RX"),
        (Mods.HalfTime, "HT"),
        (Mods.Nightcore, "NC"),
        (Mods.Flashlight, "FL"),
        (Mods.SpunOut, "SO"),
        (Mods.Perfect, "PF"),
        (Mods.FreeMod, "Freemod"),
    ];

    /// <summary>
    /// BanchoBot prints full mod names in <c>!mp settings</c> ("Team Red / HardRock") while the
    /// acronym form is what <c>!mp mods</c> takes, so both spellings are accepted on the way in.
    /// </summary>
    private static readonly (Mods Mod, string Name)[] FullNames =
    [
        (Mods.NoFail, "NoFail"),
        (Mods.Easy, "Easy"),
        (Mods.Hidden, "Hidden"),
        (Mods.HardRock, "HardRock"),
        (Mods.SuddenDeath, "SuddenDeath"),
        (Mods.DoubleTime, "DoubleTime"),
        (Mods.Relax, "Relax"),
        (Mods.HalfTime, "HalfTime"),
        (Mods.Nightcore, "Nightcore"),
        (Mods.Flashlight, "Flashlight"),
        (Mods.SpunOut, "SpunOut"),
        (Mods.Perfect, "Perfect"),
    ];

    /// <summary>Renders as space-separated acronyms in the order Bancho prints them, or <c>None</c>.</summary>
    public static string ToAcronyms(this Mods mods)
    {
        if (mods is Mods.None)
        {
            return "None";
        }

        var parts = Table.Where(entry => mods.HasFlag(entry.Mod)).Select(entry => entry.Acronym);
        return string.Join(' ', parts);
    }

    /// <summary>Compact form for UI badges, e.g. <c>HDHR</c>.</summary>
    public static string ToCompactAcronyms(this Mods mods)
    {
        if (mods is Mods.None)
        {
            return "NM";
        }

        var parts = Table
            .Where(entry => mods.HasFlag(entry.Mod) && entry.Mod is not Mods.FreeMod)
            .Select(entry => entry.Acronym);

        var joined = string.Concat(parts);
        if (mods.HasFlag(Mods.FreeMod))
        {
            joined = joined.Length == 0 ? "FM" : joined + "+FM";
        }

        return joined;
    }

    /// <summary>
    /// The acronyms as separate items. Use this where the value is a <em>set of alternatives</em>
    /// rather than one combination: "HD, EZ" reads as two mods, where <see cref="ToCompactAcronyms"/>
    /// would render "EZHD" and look like a single one.
    /// </summary>
    public static IReadOnlyList<string> ToAcronymList(this Mods mods) =>
        Table.Where(entry => mods.HasFlag(entry.Mod)).Select(entry => entry.Acronym).ToList();

    /// <summary>
    /// Parses "HDHR", "HD HR", "hd,hr" and similar. Unknown tokens are ignored so a hand-edited
    /// mappool file never fails to load over a typo.
    /// </summary>
    public static Mods ParseMods(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Mods.None;
        }

        var result = Mods.None;

        foreach (var token in text.Split([' ', ',', '+', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var word = token.Trim();
            if (word.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                word.Equals("NM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (word.Equals("Freemod", StringComparison.OrdinalIgnoreCase) ||
                word.Equals("FM", StringComparison.OrdinalIgnoreCase))
            {
                result |= Mods.FreeMod;
                continue;
            }

            var fullName = FullNames.FirstOrDefault(entry =>
                entry.Name.Equals(word, StringComparison.OrdinalIgnoreCase));

            if (fullName.Name is not null)
            {
                result |= fullName.Mod;
                continue;
            }

            // Split run-together acronyms such as HDHR.
            for (var i = 0; i + 2 <= word.Length; i += 2)
            {
                var pair = word.Substring(i, 2);
                var match = Table.FirstOrDefault(entry => entry.Acronym.Equals(pair, StringComparison.OrdinalIgnoreCase));
                if (match.Acronym is not null)
                {
                    result |= match.Mod;
                }
            }
        }

        return result;
    }
}
