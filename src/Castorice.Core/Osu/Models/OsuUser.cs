using System.Text.Json.Serialization;

namespace Castorice.Core.Osu.Models;

public sealed record OsuUser
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }

    [JsonPropertyName("country_code")]
    public string CountryCode { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public OsuCountry? Country { get; init; }

    [JsonPropertyName("cover")]
    public OsuCover? Cover { get; init; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; init; }

    [JsonPropertyName("is_supporter")]
    public bool IsSupporter { get; init; }

    [JsonPropertyName("is_restricted")]
    public bool? IsRestricted { get; init; }

    [JsonPropertyName("join_date")]
    public DateTimeOffset? JoinDate { get; init; }

    [JsonPropertyName("last_visit")]
    public DateTimeOffset? LastVisit { get; init; }

    [JsonPropertyName("playmode")]
    public string PlayMode { get; init; } = "osu";

    [JsonPropertyName("statistics")]
    public OsuUserStatistics? Statistics { get; init; }

    [JsonPropertyName("rank_history")]
    public OsuRankHistory? RankHistory { get; init; }

    [JsonPropertyName("badges")]
    public IReadOnlyList<OsuBadge> Badges { get; init; } = [];

    [JsonPropertyName("playstyle")]
    public IReadOnlyList<string>? Playstyle { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonIgnore]
    public string ProfileUrl => $"https://osu.ppy.sh/users/{Id}";

    [JsonIgnore]
    public string FlagUrl => CountryCode.Length == 2
        ? $"https://osu.ppy.sh/assets/images/flags/{ToRegionalIndicator(CountryCode)}.svg"
        : string.Empty;

    private static string ToRegionalIndicator(string code) =>
        string.Join('-', code.ToUpperInvariant().Select(c => (0x1F1A5 + c).ToString("x")));
}

public sealed record OsuCountry
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed record OsuCover
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

public sealed record OsuBadge
{
    [JsonPropertyName("awarded_at")]
    public DateTimeOffset? AwardedAt { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }
}

public sealed record OsuRankHistory
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    /// <summary>Global rank per day, oldest first, for the last 90 days.</summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<int> Data { get; init; } = [];
}

public sealed record OsuUserStatistics
{
    [JsonPropertyName("global_rank")]
    public int? GlobalRank { get; init; }

    [JsonPropertyName("country_rank")]
    public int? CountryRank { get; init; }

    [JsonPropertyName("pp")]
    public double Pp { get; init; }

    [JsonPropertyName("hit_accuracy")]
    public double Accuracy { get; init; }

    [JsonPropertyName("play_count")]
    public int PlayCount { get; init; }

    [JsonPropertyName("play_time")]
    public long? PlayTimeSeconds { get; init; }

    [JsonPropertyName("total_score")]
    public long TotalScore { get; init; }

    [JsonPropertyName("ranked_score")]
    public long RankedScore { get; init; }

    [JsonPropertyName("maximum_combo")]
    public int MaximumCombo { get; init; }

    [JsonPropertyName("replays_watched_by_others")]
    public int ReplaysWatched { get; init; }

    [JsonPropertyName("total_hits")]
    public long TotalHits { get; init; }

    [JsonPropertyName("level")]
    public OsuLevel? Level { get; init; }

    [JsonPropertyName("grade_counts")]
    public OsuGradeCounts? GradeCounts { get; init; }

    [JsonIgnore]
    public TimeSpan PlayTime => TimeSpan.FromSeconds(PlayTimeSeconds ?? 0);
}

public sealed record OsuLevel
{
    [JsonPropertyName("current")]
    public int Current { get; init; }

    [JsonPropertyName("progress")]
    public int Progress { get; init; }
}

public sealed record OsuGradeCounts
{
    [JsonPropertyName("ssh")]
    public int SilverSs { get; init; }

    [JsonPropertyName("ss")]
    public int Ss { get; init; }

    [JsonPropertyName("sh")]
    public int SilverS { get; init; }

    [JsonPropertyName("s")]
    public int S { get; init; }

    [JsonPropertyName("a")]
    public int A { get; init; }
}
