using System.Text.Json.Serialization;

namespace Castorice.Core.Osu.Models;

public sealed record OsuScore
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; init; }

    [JsonPropertyName("max_combo")]
    public int MaxCombo { get; init; }

    [JsonPropertyName("mods")]
    public IReadOnlyList<string> Mods { get; init; } = [];

    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("perfect")]
    public bool Perfect { get; init; }

    [JsonPropertyName("pp")]
    public double? Pp { get; init; }

    [JsonPropertyName("rank")]
    public string Rank { get; init; } = string.Empty;

    [JsonPropertyName("score")]
    public long Score { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("beatmap")]
    public OsuBeatmap? Beatmap { get; init; }

    [JsonPropertyName("beatmapset")]
    public OsuBeatmapset? Beatmapset { get; init; }

    [JsonPropertyName("statistics")]
    public OsuScoreStatistics? Statistics { get; init; }

    [JsonIgnore]
    public string ModsDisplay => Mods.Count == 0 ? "NM" : string.Concat(Mods);

    [JsonIgnore]
    public string Title => Beatmapset is null
        ? Beatmap?.Version ?? "Unknown beatmap"
        : $"{Beatmapset.Artist} - {Beatmapset.Title}";

    [JsonIgnore]
    public string Difficulty => Beatmap?.Version ?? string.Empty;

    [JsonIgnore]
    public string AccuracyDisplay => $"{Accuracy * 100:0.00}%";

    /// <summary>Small beatmapset cover for the score list; null when the API omitted the set.</summary>
    [JsonIgnore]
    public string? ThumbnailUrl => Beatmapset?.Covers?.List ?? Beatmapset?.Covers?.Card;

    [JsonIgnore]
    public string PpDisplay => Pp is null or 0 ? "-" : $"{Pp:0}pp";
}

public sealed record OsuScoreStatistics
{
    [JsonPropertyName("count_300")]
    public int? Count300 { get; init; }

    [JsonPropertyName("count_100")]
    public int? Count100 { get; init; }

    [JsonPropertyName("count_50")]
    public int? Count50 { get; init; }

    [JsonPropertyName("count_miss")]
    public int? CountMiss { get; init; }
}
