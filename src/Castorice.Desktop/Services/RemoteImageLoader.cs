using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Castorice.Core.Caching;
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
    private static readonly DiskByteCache Disk = new(Path.Combine(AppPaths.Root, "cache", "images"));

    public static string CacheDirectory => Disk.Directory;

    public static CacheUsage DiskUsage => Disk.GetUsage();

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

    /// <summary>
    /// Drops every cached image, on disk and in memory, and returns how many files went. Images on
    /// screen keep showing; anything shown later is simply downloaded again.
    /// </summary>
    public static int ClearCache()
    {
        Cache.Clear();
        return Disk.Clear();
    }

    private static async Task<Bitmap?> LoadAsync(string url, int decodeWidth)
    {
        var key = decodeWidth > 0 ? $"{url}|{decodeWidth}" : url;
        var load = Cache.GetOrAdd(key, _ => DownloadAsync(url, decodeWidth));

        try
        {
            return await load.ConfigureAwait(false);
        }
        catch
        {
            // A failed load must not stick for the rest of the session: forget it, so the image is
            // tried again the next time it is shown. Only this attempt is removed, not a newer one.
            Cache.TryRemove(new KeyValuePair<string, Task<Bitmap?>>(key, load));
            throw;
        }
    }

    private static async Task<Bitmap?> DownloadAsync(string url, int decodeWidth)
    {
        if (await Disk.TryReadAsync(url).ConfigureAwait(false) is { } cached)
        {
            if (TryDecode(cached, decodeWidth) is { } fromDisk)
            {
                TrimMemoryCache();
                return fromDisk;
            }

            // Bytes that are cut short or do not decode are a broken entry, not an image —
            // typically one left half-written by an older version. Drop it and fetch it again.
            Disk.Remove(url);
        }

        var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);

        // Decoded before it is stored, so an error page served with status 200 is never cached.
        var bitmap = TryDecode(bytes, decodeWidth)
            ?? throw new InvalidDataException($"{url} did not return an image.");

        await Disk.WriteAsync(url, bytes).ConfigureAwait(false);

        TrimMemoryCache();
        return bitmap;
    }

    private static Bitmap? TryDecode(byte[] bytes, int decodeWidth)
    {
        // A cut-off file still "decodes" into a mostly blank picture, so completeness is checked
        // first; otherwise one truncated download would stay cached as a grey tile for good.
        if (!ImageBytes.LooksComplete(bytes))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return decodeWidth > 0
                ? Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality)
                : new Bitmap(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The decoder's failure types are not documented; any of them means "not an image".
            return null;
        }
    }

    /// <summary>
    /// Bitmaps hold unmanaged memory, so the in-memory map is bounded. Eviction is coarse — half the
    /// entries go, in no particular order — because covers are cheap to re-decode from disk.
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
}
