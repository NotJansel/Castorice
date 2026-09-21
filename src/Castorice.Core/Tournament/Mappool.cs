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

    /// <summary>osu! usernames auto-added as referees with <c>!mp addref</c>.</summary>
    public List<string> Referees { get; set; } = [];

    public List<MappoolSlot> Slots { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<IGrouping<string, MappoolSlot>> ByCategory =>
        Slots.GroupBy(slot => slot.EffectiveCategory);

    /// <summary>The room title suggested for <c>!mp make</c>, e.g. <c>OWC: (Red) vs (Blue)</c>.</summary>
    public string BuildRoomName(string redTeam, string blueTeam)
    {
        var prefix = string.IsNullOrWhiteSpace(Acronym) ? Name : Acronym;
        if (!string.IsNullOrWhiteSpace(Stage))
        {
            prefix = $"{prefix}: {Stage}";
        }

        var red = string.IsNullOrWhiteSpace(redTeam) ? "Red" : redTeam.Trim();
        var blue = string.IsNullOrWhiteSpace(blueTeam) ? "Blue" : blueTeam.Trim();

        return $"{prefix}: ({red}) vs ({blue})";
    }
}
