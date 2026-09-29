namespace Castorice.Core.MappoolBuilder;

// The Mappool Builder's JSON, read with the web defaults (camelCase). Only the fields Castorice
// uses are declared; anything else in a response is ignored, so additions on the server side
// cannot break the import.

public sealed class RemoteOwner
{
    public long Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;
}

public sealed class RemotePoolSlot
{
    /// <summary>Stable across reordering and mod changes, so it is what links a local pick to it.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The mod bracket: NM, HD, HR, DT, FM, EZ, FL, HT, SD or TB.</summary>
    public string Mod { get; set; } = string.Empty;

    public long BeatmapId { get; set; }

    public long BeatmapsetId { get; set; }

    public string Artist { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>The difficulty name.</summary>
    public string Version { get; set; } = string.Empty;

    public string Mapper { get; set; } = string.Empty;

    /// <summary>Star rating with the bracket's mod applied.</summary>
    public double Sr { get; set; }

    public double BaseSr { get; set; }

    public double Bpm { get; set; }

    /// <summary>Drain time in seconds, rate change included.</summary>
    public int Length { get; set; }

    public string Cover { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public sealed class RemotePool
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public string TeamRed { get; set; } = string.Empty;

    public string TeamBlue { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsPublic { get; set; }

    public RemoteOwner? Owner { get; set; }

    /// <summary>Already in display order.</summary>
    public List<RemotePoolSlot> Slots { get; set; } = [];

    /// <summary>Unix time in milliseconds.</summary>
    public long CreatedAt { get; set; }

    /// <summary>Unix time in milliseconds; goes up with every change to the pool or its slots.</summary>
    public long UpdatedAt { get; set; }

    public DateTimeOffset UpdatedAtTime => DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAt);
}

/// <summary>The answer to <c>GET /api/pools</c>.</summary>
public sealed class RemotePoolList
{
    /// <summary>The caller's own pools, newest change first. Empty without a token.</summary>
    public List<RemotePool> Mine { get; set; } = [];

    /// <summary>The 24 most recently changed public pools of other users.</summary>
    public List<RemotePool> Discover { get; set; } = [];
}

public sealed class RemoteUser
{
    public long Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;
}

/// <summary>The answer to <c>GET /api/me</c>; <see cref="User"/> is <c>null</c> without a token.</summary>
public sealed class RemoteSession
{
    public RemoteUser? User { get; set; }
}

/// <summary>The error body the API sends with every failure.</summary>
internal sealed class RemoteError
{
    public int StatusCode { get; set; }

    public string? StatusMessage { get; set; }
}
