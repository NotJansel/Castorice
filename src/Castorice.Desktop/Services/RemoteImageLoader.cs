using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Castorice.Core.Configuration;

namespace Castorice.Desktop.Services;

/// <summary>
/// Loads beatmap covers and avatars into an <see cref="Image"/> from a URL, via
/// <c>services:RemoteImageLoader.Source</c>. Decoded bitmaps are kept in memory and the bytes are
/// cached on disk, so flipping between pools does not re-download the same covers.
/// </summary>
public static class RemoteImageLoader
{
    private const int MemoryCacheLimit = 200;

    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly string DiskCacheDirectory = Path.Combine(AppPaths.Root, "cache", "images");

    /// <summary>The URL to display. Setting it to null or empty clears the image.</summary>
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(RemoteImageLoader));

    /// <summary>Longest edge the bitmap is decoded to. 0 decodes at full size.</summary>
    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<Image, int>("DecodeWidth", typeof(RemoteImageLoader), 0);

    static RemoteImageLoader()
    {
        SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
    }

    public static string? GetSource(Image image) => image.GetValue(SourceProperty);

    public static void SetSource(Image image, string? value) => image.SetValue(SourceProperty, value);

    public static int GetDecodeWidth(Image image) => image.GetValue(DecodeWidthProperty);

    public static void SetDecodeWidth(Image image, int value) => image.SetValue(DecodeWidthProperty, value);

    private static async void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs args)
    {
        var url = args.GetNewValue<string?>();

        image.Source = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var decodeWidth = image.GetValue(DecodeWidthProperty);

        try
        {
            var bitmap = await LoadAsync(url, decodeWidth).ConfigureAwait(true);

            // The control may have been recycled onto a different item while the download ran.
            if (bitmap is not null && GetSource(image) == url)
            {
                image.Source = bitmap;
            }
        }
        catch (Exception)
        {
            // A missing cover is a cosmetic problem; the tile simply stays flat.
        }
    }

    private static Task<Bitmap?> LoadAsync(string url, int decodeWidth)
    {
        var key = decodeWidth > 0 ? $"{url}|{decodeWidth}" : url;

        return Cache.GetOrAdd(key, _ => DownloadAsync(url, decodeWidth));
    }

    private static async Task<Bitmap?> DownloadAsync(string url, int decodeWidth)
    {
        byte[] bytes;

        var cacheFile = Path.Combine(DiskCacheDirectory, Fingerprint(url));
        if (File.Exists(cacheFile))
        {
            bytes = await File.ReadAllBytesAsync(cacheFile).ConfigureAwait(false);
        }
        else
        {
            bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);

            try
            {
                Directory.CreateDirectory(DiskCacheDirectory);
                await File.WriteAllBytesAsync(cacheFile, bytes).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Losing the disk cache costs a re-download, nothing more.
            }
        }

        using var stream = new MemoryStream(bytes);
        var bitmap = decodeWidth > 0
            ? Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality)
            : new Bitmap(stream);

        TrimMemoryCache();
        return bitmap;
    }

    /// <summary>
    /// Bitmaps hold unmanaged memory, so the in-memory map is bounded. Eviction is coarse — the
    /// whole map is dropped — because covers are cheap to re-decode from the disk cache.
    /// </summary>
    private static void TrimMemoryCache()
    {
        if (Cache.Count <= MemoryCacheLimit)
        {
            return;
        }

        foreach (var key in Cache.Keys.Take(Cache.Count - (MemoryCacheLimit / 2)))
        {
            Cache.TryRemove(key, out _);
        }
    }

    private static string Fingerprint(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexStringLower(hash)[..32];
    }
}
