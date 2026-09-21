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

    public void Delete(string fileName)
    {
        var path = Path.Combine(_directory, Path.GetFileName(fileName));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Turns a pool name into a file name that is safe on all three target platforms.</summary>
    public static string SuggestFileName(string poolName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(poolName
            .Select(c => invalid.Contains(c) || c is ' ' ? '-' : c)
            .ToArray())
            .Trim('-');

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        return (cleaned.Length == 0 ? "mappool" : cleaned.ToLowerInvariant()) + ".json";
    }

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
