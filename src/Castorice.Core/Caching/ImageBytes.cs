namespace Castorice.Core.Caching;

/// <summary>
/// Structural checks on image files. A decoder is not enough to catch a cut-off file: Skia turns a
/// truncated JPEG into a mostly blank picture without complaint, so the end of the file is checked
/// for the marker every complete image of that format carries.
/// </summary>
public static class ImageBytes
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The IEND chunk type and its fixed CRC, which close every PNG.</summary>
    private static readonly byte[] PngEnd = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    private static readonly byte[] JpegEnd = [0xFF, 0xD9];

    /// <summary>Some encoders pad after a JPEG's end marker, so it is looked for near the end.</summary>
    private const int JpegTrailingSlack = 64;

    /// <summary>
    /// False when the bytes are a known image format that has been cut short. Formats it does not
    /// recognise pass, and are left to the decoder to judge.
    /// </summary>
    public static bool LooksComplete(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
        {
            return false;
        }

        // JPEG: starts FF D8, ends FF D9. Inside the compressed data a real FF is always followed by
        // 00, so FF D9 cannot turn up there by accident.
        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            var tail = bytes[Math.Max(0, bytes.Length - JpegTrailingSlack)..];
            return tail.IndexOf(JpegEnd) >= 0;
        }

        if (bytes.StartsWith(PngSignature))
        {
            return bytes.EndsWith(PngEnd);
        }

        // GIF: "GIF8", closed by the trailer byte 0x3B.
        if (bytes is [0x47, 0x49, 0x46, 0x38, ..])
        {
            return bytes[^1] == 0x3B;
        }

        // WebP: "RIFF" <little-endian size> "WEBP"; the size counts everything after the first 8 bytes.
        if (bytes.Length >= 12 && bytes is [0x52, 0x49, 0x46, 0x46, ..] && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            var declared = BitConverter.ToUInt32(bytes[4..8]);
            return bytes.Length >= declared + 8L;
        }

        return true;
    }
}
