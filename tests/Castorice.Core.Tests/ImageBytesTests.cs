using Castorice.Core.Caching;

namespace Castorice.Core.Tests;

public class ImageBytesTests
{
    private static byte[] Jpeg(int bodyLength, bool complete)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0 };
        bytes.AddRange(Enumerable.Repeat((byte)0x42, bodyLength));
        if (complete)
        {
            bytes.AddRange([0xFF, 0xD9]);
        }

        return [.. bytes];
    }

    [Fact]
    public void A_complete_jpeg_passes() => Assert.True(ImageBytes.LooksComplete(Jpeg(5000, complete: true)));

    [Fact]
    public void A_jpeg_cut_off_part_way_fails()
    {
        // What a write interrupted by a crash leaves behind: the start of the file and nothing more.
        var cut = Jpeg(5000, complete: true)[..400];

        Assert.False(ImageBytes.LooksComplete(cut));
    }

    [Fact]
    public void A_jpeg_padded_after_its_end_marker_still_passes()
    {
        byte[] padded = [.. Jpeg(1000, complete: true), 0, 0, 0, 0];

        Assert.True(ImageBytes.LooksComplete(padded));
    }

    [Fact]
    public void A_png_needs_its_closing_chunk()
    {
        byte[] start = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        byte[] end = [0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

        Assert.True(ImageBytes.LooksComplete([.. start, .. end]));
        Assert.False(ImageBytes.LooksComplete(start));
    }

    [Fact]
    public void A_gif_needs_its_trailer()
    {
        byte[] gif = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 5, 5, 5];

        Assert.False(ImageBytes.LooksComplete(gif));
        Assert.True(ImageBytes.LooksComplete([.. gif, 0x3B]));
    }

    [Fact]
    public void A_webp_is_checked_against_its_declared_size()
    {
        byte[] Webp(uint declared, int actualAfterHeader)
        {
            var size = BitConverter.GetBytes(declared);
            return [0x52, 0x49, 0x46, 0x46, .. size, 0x57, 0x45, 0x42, 0x50, .. new byte[actualAfterHeader]];
        }

        Assert.True(ImageBytes.LooksComplete(Webp(declared: 20, actualAfterHeader: 16)));
        Assert.False(ImageBytes.LooksComplete(Webp(declared: 2000, actualAfterHeader: 16)));
    }

    [Fact]
    public void An_unrecognised_format_is_left_to_the_decoder()
    {
        Assert.True(ImageBytes.LooksComplete("<html>not an image</html>"u8));
    }

    [Fact]
    public void Too_few_bytes_to_be_anything_fails()
    {
        Assert.False(ImageBytes.LooksComplete([0xFF, 0xD8]));
        Assert.False(ImageBytes.LooksComplete([]));
    }
}
