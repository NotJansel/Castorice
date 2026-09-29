using System.Text.Json;
using Castorice.Core.Configuration;

namespace Castorice.Core.Tournament;

public sealed record MappoolFile(string FileName, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Loads and saves mappools as one JSON file each, so a pool can be shared with the rest of the
/// staff by sending a single file.
/// </summary>
public sealed class MappoolStore(string? directory = null)
{
    private readonly string _directory = directory ?? AppPaths.MappoolDirectory;

    public string Directory => _directory;

    public IReadOnlyList<MappoolFile> List()
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        return System.IO.Directory
            .EnumerateFiles(_directory, "*.json")
            .Select(path => new MappoolFile(Path.GetFileName(path), ReadDisplayName(path)))
            .OrderBy(file => file.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Mappool? Load(string fileName)
    {
        var path = Path.Combine(_directory, Path.GetFileName(fileName));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Mappool>(File.ReadAllText(path), CastoriceJson.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Saves the pool and returns the file name it was written to.</summary>
    public string Save(Mappool pool, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        System.IO.Directory.CreateDirectory(_directory);

        var name = Path.GetFileName(fileName ?? SuggestFileName(pool.Name));
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name += ".json";
        }

        var path = Path.Combine(_directory, name);
        var temp = path + ".tmp";

        File.WriteAllText(temp, JsonSerializer.Serialize(pool, CastoriceJson.Options));
        File.Move(temp, path, overwrite: true);

        return name;
    }

    /// <summary>
    /// The file already holding the Mappool Builder pool with this id, so importing it twice
    /// updates that file instead of making a second copy.
    /// </summary>
    public string? FindBySource(string baseUrl, string poolId)
    {
        var host = HostOf(baseUrl);

        return List()
            .Select(file => file.FileName)
            .FirstOrDefault(fileName => Load(fileName)?.Source is { } source &&
                source.PoolId == poolId &&
                string.Equals(HostOf(source.BaseUrl), host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A file name for a new pool that does not overwrite an existing one.</summary>
    public string SuggestUnusedFileName(string poolName)
    {
        var suggested = SuggestFileName(poolName);
        var stem = Path.GetFileNameWithoutExtension(suggested);

        var candidate = suggested;
        for (var n = 2; File.Exists(Path.Combine(_directory, candidate)); n++)
        {
            candidate = $"{stem}-{n}.json";
        }

        return candidate;
    }

    public void Delete(string fileName)
    {
        var path = Path.Combine(_directory, Path.GetFileName(fileName));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Turns a pool name into a file name that is safe on all three target platforms. The set is
    /// hard-coded rather than taken from <see cref="Path.GetInvalidFileNameChars"/>, which on Unix
    /// reports only '/' and would let a name through that Windows then rejects.
    /// </summary>
    public static string SuggestFileName(string poolName)
    {
        const string invalid = "<>:\"/\\|?*";

        var cleaned = new string(poolName
            .Select(c => invalid.Contains(c) || char.IsControl(c) || c is ' ' ? '-' : c)
            .ToArray())
            .Trim('-');

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        return (cleaned.Length == 0 ? "mappool" : cleaned.ToLowerInvariant()) + ".json";
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : url.Trim().TrimEnd('/');

    private static string ReadDisplayName(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (document.RootElement.TryGetProperty("name", out var name) &&
                name.ValueKind is JsonValueKind.String &&
                name.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Fall through to the file name.
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}
