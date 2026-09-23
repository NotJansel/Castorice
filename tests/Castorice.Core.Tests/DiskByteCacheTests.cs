using Castorice.Core.Caching;

namespace Castorice.Core.Tests;

public class DiskByteCacheTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "castorice-cache-" + Guid.NewGuid().ToString("N"));

    private DiskByteCache Cache => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Reads_back_what_it_stored()
    {
        await Cache.WriteAsync("https://assets.ppy.sh/a.jpg", [1, 2, 3]);

        Assert.Equal([1, 2, 3], await Cache.TryReadAsync("https://assets.ppy.sh/a.jpg"));
    }

    [Fact]
    public async Task Survives_a_new_instance_which_is_what_makes_it_a_disk_cache()
    {
        await new DiskByteCache(_directory).WriteAsync("key", [9]);

        Assert.Equal([9], await new DiskByteCache(_directory).TryReadAsync("key"));
    }

    [Fact]
    public async Task A_missing_entry_reads_as_null()
    {
        Assert.Null(await Cache.TryReadAsync("never stored"));
    }

    [Fact]
    public async Task Leaves_no_temporary_files_behind()
    {
        await Cache.WriteAsync("a", [1]);
        await Cache.WriteAsync("a", [2]);
        await Cache.WriteAsync("b", [3]);

        Assert.DoesNotContain(Directory.EnumerateFiles(_directory), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Equal(2, Directory.EnumerateFiles(_directory).Count());
    }

    [Fact]
    public async Task Overwriting_an_entry_replaces_it()
    {
        await Cache.WriteAsync("a", [1, 1, 1]);
        await Cache.WriteAsync("a", [2]);

        Assert.Equal([2], await Cache.TryReadAsync("a"));
    }

    [Fact]
    public async Task Keeps_the_file_names_older_versions_wrote()
    {
        // Files from before this class existed must stay valid after an update.
        var path = Cache.PathFor("https://assets.ppy.sh/beatmaps/1/covers/card.jpg");

        Assert.Equal(32, Path.GetFileName(path).Length);
        Assert.Matches("^[0-9a-f]{32}$", Path.GetFileName(path));

        await Cache.WriteAsync("https://assets.ppy.sh/beatmaps/1/covers/card.jpg", [7]);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Reports_how_much_it_holds()
    {
        Assert.Equal(new CacheUsage(0, 0), Cache.GetUsage());

        await Cache.WriteAsync("a", new byte[1000]);
        await Cache.WriteAsync("b", new byte[500]);

        Assert.Equal(new CacheUsage(2, 1500), Cache.GetUsage());
    }

    [Fact]
    public async Task Clearing_removes_every_entry()
    {
        await Cache.WriteAsync("a", [1]);
        await Cache.WriteAsync("b", [2]);

        Assert.Equal(2, Cache.Clear());
        Assert.Equal(new CacheUsage(0, 0), Cache.GetUsage());
        Assert.Null(await Cache.TryReadAsync("a"));
    }

    [Fact]
    public void Clearing_a_cache_that_was_never_written_is_fine()
    {
        Assert.Equal(0, Cache.Clear());
    }

    [Fact]
    public async Task Removing_one_entry_leaves_the_rest()
    {
        await Cache.WriteAsync("broken", [0]);
        await Cache.WriteAsync("fine", [1]);

        Cache.Remove("broken");

        Assert.Null(await Cache.TryReadAsync("broken"));
        Assert.Equal([1], await Cache.TryReadAsync("fine"));
    }

    [Theory]
    [InlineData(0, 0, "Empty")]
    [InlineData(1, 900, "1 image · 900 B")]
    [InlineData(12, 1536, "12 images · 1.5 KB")]
    [InlineData(40, 5_452_595, "40 images · 5.2 MB")]
    public void Describes_its_size_for_the_settings_page(int files, long bytes, string expected) =>
        Assert.Equal(expected, new CacheUsage(files, bytes).Describe());
}
