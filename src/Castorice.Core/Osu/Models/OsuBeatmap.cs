using System.Text.Json.Serialization;

namespace Castorice.Core.Osu.Models;

public sealed record OsuBeatmap
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("beatmapset_id")]
    public long BeatmapsetId { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("difficulty_rating")]
    public double DifficultyRating { get; init; }

    [JsonPropertyName("bpm")]
    public double? Bpm { get; init; }

    [JsonPropertyName("total_length")]
    public int TotalLength { get; init; }

    [JsonPropertyName("hit_length")]
    public int HitLength { get; init; }

    [JsonPropertyName("cs")]
    public double CircleSize { get; init; }

    [JsonPropertyName("ar")]
    public double ApproachRate { get; init; }

    [JsonPropertyName("accuracy")]
    public double OverallDifficulty { get; init; }

    [JsonPropertyName("drain")]
    public double HpDrain { get; init; }

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "osu";

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("beatmapset")]
    public OsuBeatmapset? Beatmapset { get; init; }
}

public sealed record OsuBeatmapset
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("artist")]
    public string Artist { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("creator")]
    public string Creator { get; init; } = string.Empty;

    [JsonPropertyName("covers")]
    public OsuCovers? Covers { get; init; }
}

public sealed record OsuCovers
{
    [JsonPropertyName("cover")]
    public string? Cover { get; init; }

    [JsonPropertyName("card")]
    public string? Card { get; init; }

    [JsonPropertyName("list")]
    public string? List { get; init; }

    [JsonPropertyName("slimcover")]
    public string? SlimCover { get; init; }
}
