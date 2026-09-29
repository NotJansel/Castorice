using Castorice.Core.Tournament;

namespace Castorice.Core.MappoolBuilder;

/// <summary>What an update from the Mappool Builder changed.</summary>
public sealed record PoolRefreshResult(int Added, int Removed, int Changed, int Kept)
{
    public bool HasChanges => Added > 0 || Removed > 0 || Changed > 0;

    public string Describe()
    {
        if (!HasChanges)
        {
            return "no map changes";
        }

        var parts = new List<string>(3);
        if (Added > 0)
        {
            parts.Add($"{Added} added");
        }

        if (Removed > 0)
        {
            parts.Add($"{Removed} removed");
        }

        if (Changed > 0)
        {
            parts.Add($"{Changed} changed");
        }

        return string.Join(", ", parts);
    }
}

/// <summary>
/// Turns a Mappool Builder pool into a Castorice pool. The Builder owns the maps; everything
/// Castorice adds on top (room settings, draft order, multipliers, referees) stays local and
/// survives an update.
/// </summary>
public static class MappoolBuilderImport
{
    // The Builder's brackets in its own display order, with the mods and group name each maps to.
    // FM and TB are both FreeMod; NoFail is added when the pick is sent, per the pool's setting.
    private static readonly (string Key, Mods Mods, string Category)[] Brackets =
    [
        ("NM", Mods.None, "NoMod"),
        ("HD", Mods.Hidden, "Hidden"),
        ("HR", Mods.HardRock, "HardRock"),
        ("DT", Mods.DoubleTime, "DoubleTime"),
        ("FM", Mods.FreeMod, "FreeMod"),
        ("EZ", Mods.Easy, "Easy"),
        ("FL", Mods.Flashlight, "Flashlight"),
        ("HT", Mods.HalfTime, "HalfTime"),
        ("SD", Mods.SuddenDeath, "SuddenDeath"),
        ("TB", Mods.FreeMod, "Tiebreaker"),
    ];

    public static Mappool ToMappool(RemotePool remote, MappoolBuilderConnection connection)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(connection);

        return new Mappool
        {
            Name = string.IsNullOrWhiteSpace(remote.Name) ? $"Mappool {remote.Id}" : remote.Name.Trim(),
            Stage = remote.Stage.Trim(),
            Slots = BuildSlots(remote.Slots),
            Source = SourceOf(remote, connection),
        };
    }

    /// <summary>
    /// Replaces the pool's maps with the Builder's current ones. A pick that is still there,
    /// matched by its slot id, keeps its local multiplier override.
    /// </summary>
    public static PoolRefreshResult Refresh(Mappool local, RemotePool remote, MappoolBuilderConnection connection)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(connection);

        var previous = local.Slots
            .Where(s => !string.IsNullOrEmpty(s.RemoteId))
            .GroupBy(s => s.RemoteId!)
            .ToDictionary(g => g.Key, g => g.First());

        var slots = BuildSlots(remote.Slots);
        int added = 0, changed = 0, kept = 0;

        foreach (var slot in slots)
        {
            if (slot.RemoteId is null || !previous.TryGetValue(slot.RemoteId, out var old))
            {
                added++;
                continue;
            }

            slot.EasyMultiplier = old.EasyMultiplier;
            slot.EasyHiddenMultiplier = old.EasyHiddenMultiplier;

            if (old.BeatmapId != slot.BeatmapId || old.Mods != slot.Mods)
            {
                changed++;
            }
            else
            {
                kept++;
            }
        }

        var remoteIds = slots.Select(s => s.RemoteId).ToHashSet();
        var removed = local.Slots.Count(s => s.RemoteId is null || !remoteIds.Contains(s.RemoteId));

        local.Slots = slots;
        local.Source = SourceOf(remote, connection);

        return new PoolRefreshResult(added, removed, changed, kept);
    }

    /// <summary>
    /// Labels are counted per bracket in the Builder's order (NM1, NM2, HD1, …), the same way its
    /// own pages name them, so staff talk about the same picks. A lone tiebreaker is just "TB".
    /// The slots are then grouped in the Builder's bracket order.
    /// </summary>
    public static List<MappoolSlot> BuildSlots(IReadOnlyList<RemotePoolSlot> remoteSlots)
    {
        ArgumentNullException.ThrowIfNull(remoteSlots);

        var perBracket = remoteSlots
            .GroupBy(s => BracketKey(s.Mod))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        var labelled = remoteSlots.Select((remote, index) =>
        {
            var key = BracketKey(remote.Mod);
            var number = counters[key] = counters.GetValueOrDefault(key) + 1;
            var label = key == "TB" && perBracket[key] == 1 ? "TB" : $"{key}{number}";
            return (Slot: ToSlot(remote, key, label), Order: BracketOrder(key), Index: index);
        });

        return labelled
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Index)
            .Select(x => x.Slot)
            .ToList();
    }

    private static MappoolSlot ToSlot(RemotePoolSlot remote, string key, string label)
    {
        var (mods, category) = BracketFor(key);

        return new MappoolSlot
        {
            Label = label,
            BeatmapId = remote.BeatmapId,
            Title = remote.Title,
            Artist = remote.Artist,
            Difficulty = remote.Version,
            Mapper = remote.Mapper,
            StarRating = remote.Sr,
            Bpm = remote.Bpm,
            LengthSeconds = remote.Length,
            CoverUrl = remote.Cover,
            Mods = mods,
            Category = category,
            Notes = remote.Note ?? string.Empty,
            RemoteId = string.IsNullOrEmpty(remote.Id) ? null : remote.Id,
        };
    }

    private static MappoolSource SourceOf(RemotePool remote, MappoolBuilderConnection connection) => new()
    {
        BaseUrl = connection.Origin.GetLeftPart(UriPartial.Authority),
        PoolId = remote.Id,
        OwnerName = remote.Owner?.Username ?? string.Empty,
        UpdatedAt = remote.UpdatedAt,
    };

    /// <summary>The Builder turns unknown brackets into NM itself; this does the same for safety.</summary>
    private static string BracketKey(string? mod)
    {
        var key = (mod ?? string.Empty).Trim().ToUpperInvariant();
        return Array.Exists(Brackets, b => b.Key == key) ? key : "NM";
    }

    private static (Mods Mods, string Category) BracketFor(string key)
    {
        var bracket = Array.Find(Brackets, b => b.Key == key);
        return (bracket.Mods, bracket.Category);
    }

    private static int BracketOrder(string key) => Array.FindIndex(Brackets, b => b.Key == key);
}
