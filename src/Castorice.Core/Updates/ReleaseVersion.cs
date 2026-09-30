using System.Globalization;

namespace Castorice.Core.Updates;

/// <summary>
/// A semantic version as used in release tags: <c>v1.2.3</c>, <c>1.2.3</c> or <c>1.2.3-beta.1</c>.
/// A pre-release sorts below the release it leads up to.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, string preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The part after the dash, e.g. <c>beta.1</c>; empty for a release.</summary>
    public string PreRelease { get; }

    public bool IsPreRelease => PreRelease.Length > 0;

    public static ReleaseVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        // Build metadata (+sha) does not take part in ordering.
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var core = dash >= 0 ? value[..dash] : value;
        var pre = dash >= 0 ? value[(dash + 1)..] : string.Empty;

        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3 || (dash >= 0 && pre.Length == 0))
        {
            return null;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryParseNumber(parts[i], out numbers[i]))
            {
                return null;
            }
        }

        // Pre-release identifiers are letters, digits and hyphens; numeric ones without leading
        // zeros, so "1" and "01" can never be two spellings of the same version.
        if (pre.Length > 0 && pre.Split('.').Any(id =>
                id.Length == 0 ||
                !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ||
                (id.All(char.IsAsciiDigit) && !TryParseNumber(id, out _))))
        {
            return null;
        }

        return new ReleaseVersion(numbers[0], numbers[1], numbers[2], pre);
    }

    private static bool TryParseNumber(string text, out int number)
    {
        number = 0;
        return (text.Length == 1 || !text.StartsWith('0')) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        return (IsPreRelease, other.IsPreRelease) switch
        {
            (false, false) => 0,
            (false, true) => 1,
            (true, false) => -1,
            _ => ComparePreRelease(PreRelease, other.PreRelease),
        };
    }

    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    public override string ToString() =>
        IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Dot-separated identifiers: numbers compare numerically and sort below words.</summary>
    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);

            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);
    }
}
