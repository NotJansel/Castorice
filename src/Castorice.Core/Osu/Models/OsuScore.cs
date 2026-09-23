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

    /// <summary>Acronyms, whichever of the API's two shapes the mods arrived in.</summary>
    [JsonPropertyName("mods")]
    [JsonConverter(typeof(OsuModListConverter))]
    public IReadOnlyList<string> Mods { get; init; } = [];

    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("pp")]
    public double? Pp { get; init; }

    [JsonPropertyName("rank")]
    public string Rank { get; init; } = string.Empty;

    // The API's score payload changed shape with x-api-version 20220705. Both spellings are read
    // and the properties below resolve them, so neither version leaves a field silently at zero.

    [JsonPropertyName("score")]
    public long? LegacyFormatScore { get; init; }

    /// <summary>The stable score for a score set on stable; 0 for one set on lazer.</summary>
    [JsonPropertyName("legacy_total_score")]
    public long? LegacyTotalScore { get; init; }

    [JsonPropertyName("total_score")]
    public long? TotalScore { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("ended_at")]
    public DateTimeOffset? EndedAt { get; init; }

    [JsonPropertyName("perfect")]
    public bool? LegacyFormatPerfect { get; init; }

    [JsonPropertyName("legacy_perfect")]
    public bool? LegacyPerfect { get; init; }

    [JsonPropertyName("is_perfect_combo")]
    public bool? IsPerfectCombo { get; init; }

    /// <summary>The score as the osu! website shows it: stable's number where there is one.</summary>
    [JsonIgnore]
    public long Score => LegacyTotalScore is > 0
        ? LegacyTotalScore.Value
        : TotalScore ?? LegacyFormatScore ?? 0;

    [JsonIgnore]
    public DateTimeOffset? PlayedAt => EndedAt ?? CreatedAt;

    [JsonIgnore]
    public bool Perfect => LegacyPerfect ?? IsPerfectCombo ?? LegacyFormatPerfect ?? false;

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
