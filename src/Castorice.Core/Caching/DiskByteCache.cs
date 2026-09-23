using System.Security.Cryptography;
using System.Text;

namespace Castorice.Core.Caching;

/// <summary>How much a cache holds, for the Settings page.</summary>
public readonly record struct CacheUsage(int Files, long Bytes)
{
    public string Describe() => Files == 0
        ? "Empty"
        : $"{Files} {(Files == 1 ? "image" : "images")} · {FormatBytes(Bytes)}";

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };
}

/// <summary>
/// A folder of files keyed by an arbitrary string (a URL, say). Entries are written through a
/// temporary file and moved into place, so a crash mid-write can never leave a truncated entry
/// that later reads back as a broken image.
/// </summary>
public sealed class DiskByteCache(string directory)
{
    private const string TempSuffix = ".tmp";

    public string Directory { get; } = directory;

    /// <summary>Returns the cached bytes, or <c>null</c> when there is no entry or it cannot be read.</summary>
    public async Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores an entry. Failure is swallowed on purpose: a cache that cannot be written only costs
    /// a download later, which is no reason to fail the caller.
    /// </summary>
    public async Task WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var path = PathFor(key);

        // Unique per write, so two concurrent writes of the same key cannot share a temp file.
        var temp = $"{path}.{Guid.NewGuid():N}{TempSuffix}";

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            TryDelete(temp);
        }
    }

    /// <summary>Drops one entry, e.g. because its bytes turned out not to decode.</summary>
    public void Remove(string key) => TryDelete(PathFor(key));

    public CacheUsage GetUsage()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return new CacheUsage(0, 0);
        }

        var files = 0;
        long bytes = 0;

        foreach (var file in new DirectoryInfo(Directory).EnumerateFiles())
        {
            if (file.Name.EndsWith(TempSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            files++;
            bytes += file.Length;
        }

        return new CacheUsage(files, bytes);
    }

    /// <summary>
    /// Deletes every entry and returns how many went. A file another process holds open is left
    /// for next time rather than failing the whole clear.
    /// </summary>
    public int Clear()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
        {
            if (TryDelete(file) && !file.EndsWith(TempSuffix, StringComparison.Ordinal))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>A stable, filesystem-safe name for a key: the first 32 hex digits of its SHA-256.</summary>
    public string PathFor(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(Directory, Convert.ToHexStringLower(hash)[..32]);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
