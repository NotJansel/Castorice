using System.Text.Json.Serialization;

namespace Castorice.Core.Tournament;

public enum PlayMode
{
    Osu = 0,
    Taiko = 1,
    Catch = 2,
    Mania = 3,
}

/// <summary>One pick in a pool, e.g. <c>NM1</c>. A slot is what a button in the UI maps onto.</summary>
public sealed class MappoolSlot
{
    /// <summary>Bracket label shown on the button, e.g. <c>NM1</c>, <c>HD2</c>, <c>TB</c>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The beatmap (difficulty) id — the trailing number of <c>osu.ppy.sh/b/…</c>.</summary>
    public long BeatmapId { get; set; }

    /// <summary>Filled in from the osu! API, or hand-written, purely for display.</summary>
    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Difficulty { get; set; } = string.Empty;

    public string Mapper { get; set; } = string.Empty;

    public double StarRating { get; set; }

    /// <summary>Drain length in seconds; 0 when unknown.</summary>
    public int LengthSeconds { get; set; }

    public double Bpm { get; set; }

    /// <summary>Beatmapset cover, filled in from the osu! API. Empty means the tile draws flat.</summary>
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>
    /// Score multiplier for Easy on this pick, overriding the pool's default. <c>null</c> keeps the
    /// default, so a bracket with one rule does not have to repeat it on every FreeMod map.
    /// Only ever read for a FreeMod pick.
    /// </summary>
    public double? EasyMultiplier { get; set; }

    /// <summary>Per-pick override for Easy combined with Hidden. <c>null</c> keeps the pool default.</summary>
    public double? EasyHiddenMultiplier { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<Mods>))]
    public Mods Mods { get; set; } = Mods.None;

    /// <summary>Free-form bracket grouping, e.g. <c>NoMod</c> or <c>Tiebreaker</c>. Drives button colours.</summary>
    public string Category { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => (Title.Length, BeatmapId, Difficulty.Length) switch
    {
        (0, <= 0, _) => "No beatmap set",
        (0, _, _) => $"Beatmap {BeatmapId}",
        (_, _, 0) => Title,
        _ => $"{Title} [{Difficulty}]",
    };

    [JsonIgnore]
    public string DisplayLength =>
        LengthSeconds <= 0 ? string.Empty : TimeSpan.FromSeconds(LengthSeconds).ToString(@"m\:ss");

    /// <summary>The category if one is set, otherwise the letters leading the label (<c>HD2</c> → <c>HD</c>).</summary>
    public string EffectiveCategory
    {
        get
        {
            if (Category.Length > 0)
            {
                return Category;
            }

            var letters = new string(Label.TakeWhile(char.IsLetter).ToArray());
            return letters.Length > 0 ? letters.ToUpperInvariant() : "MISC";
        }
    }

    /// <summary>True when this pick carries a multiplier of its own rather than the pool's.</summary>
    [JsonIgnore]
    public bool HasMultiplierOverride => EasyMultiplier is not null || EasyHiddenMultiplier is not null;

    /// <summary>
    /// The multipliers to score this pick with: its own where set, the pool's default otherwise.
    /// </summary>
    public ScoreMultipliers MultipliersOrDefault(ScoreMultipliers poolDefault)
    {
        ArgumentNullException.ThrowIfNull(poolDefault);

        return new ScoreMultipliers(
            EasyMultiplier ?? poolDefault.Easy,
            EasyHiddenMultiplier ?? poolDefault.EasyHidden);
    }

    /// <summary>Drops both overrides so the pick follows the pool default again.</summary>
    public void ClearMultiplierOverride()
    {
        EasyMultiplier = null;
        EasyHiddenMultiplier = null;
    }

    public MappoolSlot Clone() => (MappoolSlot)MemberwiseClone();
}

/// <summary>A configured pool plus the room defaults used when opening a lobby for it.</summary>
public sealed class Mappool
{
    public string Name { get; set; } = "New Mappool";

    /// <summary>Tournament acronym, used in the generated room name, e.g. <c>OWC</c>.</summary>
    public string Acronym { get; set; } = string.Empty;

    /// <summary>Stage label used in the generated room name, e.g. <c>Quarterfinals</c>.</summary>
    public string Stage { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<PlayMode>))]
    public PlayMode PlayMode { get; set; } = PlayMode.Osu;

    [JsonConverter(typeof(JsonStringEnumConverter<TeamMode>))]
    public TeamMode TeamMode { get; set; } = TeamMode.TeamVs;

    [JsonConverter(typeof(JsonStringEnumConverter<ScoreMode>))]
    public ScoreMode ScoreMode { get; set; } = ScoreMode.ScoreV2;

    /// <summary>Slot count passed to <c>!mp set</c>; 0 leaves the room default.</summary>
    public int RoomSize { get; set; } = 16;

    /// <summary>Seconds passed to <c>!mp start</c> when a match is started from the toolbar.</summary>
    public int StartTimerSeconds { get; set; } = 10;

    /// <summary>Seconds passed to <c>!mp timer</c> for the pick/ready countdown.</summary>
    public int ReadyTimerSeconds { get; set; } = 120;

    /// <summary>
    /// Default Easy multiplier for this pool's FreeMod picks. 1.75x is the usual bracket rule.
    /// A pick may override it; set both to 1 to score FreeMod picks raw.
    /// </summary>
    public double EasyMultiplier { get; set; } = 1.75;

    /// <summary>
    /// Default multiplier for Easy combined with Hidden, which many brackets set lower than plain
    /// Easy because Hidden already carries its own ScoreV2 bonus. A pick may override it.
    /// </summary>
    public double EasyHiddenMultiplier { get; set; } = 1.75;

    /// <summary>Maps played in a match; the target is <c>BestOf / 2 + 1</c> points.</summary>
    public int BestOf { get; set; } = 13;

    /// <summary>
    /// The mods a player may take on a FreeMod pick. The check reports anyone carrying something
    /// outside this set.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Mods>))]
    public Mods FreeModAllowedMods { get; set; } = FreeModCheck.DefaultAllowed;

    /// <summary>
    /// What each team has to field on a FreeMod pick. The default is one HardRock and one
    /// Hidden/Easy player; whoever is left over may play NoMod, which is why a 3v3 team has one
    /// NoMod slot spare and a 4v4 team two.
    /// </summary>
    public List<FreeModGroup> FreeModGroups { get; set; } = FreeModCheck.DefaultGroups();

    /// <summary>
    /// Adds NoFail to every pick that is not FreeMod, whatever its slot, so a player who fails
    /// still posts a score. On a FreeMod pick players choose their own mods, NoFail included.
    /// </summary>
    public bool ForceNoFail { get; set; } = true;

    /// <summary>The mods a pick is sent to the lobby with, NoFail included where the pool adds it.</summary>
    public Mods ModsToSend(MappoolSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return ForceNoFail && !slot.Mods.HasFlag(Mods.FreeMod)
            ? slot.Mods | Mods.NoFail
            : slot.Mods;
    }

    /// <summary>Protect, ban and pick order for matches played on this pool.</summary>
    public DraftRules Draft { get; set; } = new();

    /// <summary>osu! usernames auto-added as referees with <c>!mp addref</c>.</summary>
    public List<string> Referees { get; set; } = [];

    /// <summary>The pool-wide default, used by any pick that does not override it.</summary>
    [JsonIgnore]
    public ScoreMultipliers Multipliers => new(EasyMultiplier, EasyHiddenMultiplier);

    /// <summary>The multipliers a given pick is scored with, falling back to this pool's default.</summary>
    public ScoreMultipliers MultipliersFor(MappoolSlot? slot) =>
        slot?.MultipliersOrDefault(Multipliers) ?? Multipliers;

    /// <summary>The FreeMod picks, which are the only ones a multiplier ever applies to.</summary>
    [JsonIgnore]
    public IEnumerable<MappoolSlot> FreeModSlots => Slots.Where(s => s.Mods.HasFlag(Mods.FreeMod));

    [JsonIgnore]
    public int PointsToWin => Math.Max(1, (BestOf / 2) + 1);

    public List<MappoolSlot> Slots { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<IGrouping<string, MappoolSlot>> ByCategory =>
        Slots.GroupBy(slot => slot.EffectiveCategory);

    /// <summary>
    /// Whether the stage appears in the lobby title. Brackets normally leave it out, so this is
    /// off by default and the title reads <c>TP: (Red) vs (Blue)</c>.
    /// </summary>
    public bool IncludeStageInRoomName { get; set; }

    /// <summary>The room title suggested for <c>!mp make</c>, e.g. <c>OWC: (Red) vs (Blue)</c>.</summary>
    public string BuildRoomName(string redTeam, string blueTeam)
    {
        var prefix = string.IsNullOrWhiteSpace(Acronym) ? Name : Acronym;
        if (IncludeStageInRoomName && !string.IsNullOrWhiteSpace(Stage))
        {
            prefix = $"{prefix}: {Stage}";
        }

        var red = string.IsNullOrWhiteSpace(redTeam) ? "Red" : redTeam.Trim();
        var blue = string.IsNullOrWhiteSpace(blueTeam) ? "Blue" : blueTeam.Trim();

        return $"{prefix}: ({red}) vs ({blue})";
    }
}
