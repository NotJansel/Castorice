using System.Reflection;

namespace Castorice.Desktop;

/// <summary>The app's name, version and home, shown in the macOS menu and the About window.</summary>
public static class AppInfo
{
    public const string Name = "Castorice";

    public const string Tagline = "osu! IRC client with tournament tools";

    public const string Repository = "https://github.com/NotJansel/Castorice";

    /// <summary>Where releases, and so updates, come from.</summary>
    public const string GitHubRepository = "NotJansel/Castorice";

    /// <summary>
    /// The version the build was stamped with: <c>0.2.0</c> for a release, <c>0.2.0-dev</c> for a
    /// build from source. The SDK appends the commit as <c>+sha</c>; that part is left off.
    /// </summary>
    public static string Version { get; }

    /// <summary>The commit the build was made from, shortened; empty when the SDK did not know it.</summary>
    public static string Commit { get; }

    /// <summary>
    /// Built from source rather than from a release tag. Such a build does not look for updates
    /// on its own, since every release would pass for one.
    /// </summary>
    public static bool IsDevelopmentBuild { get; }

    /// <summary>The version as shown to people: a development build also names its commit.</summary>
    public static string DisplayVersion =>
        IsDevelopmentBuild && Commit.Length > 0 ? $"{Version} ({Commit})" : Version;

    static AppInfo()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            informational = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        Version = plus < 0 ? informational : informational[..plus];

        var commit = plus < 0 ? string.Empty : informational[(plus + 1)..];
        Commit = commit.Length > 7 ? commit[..7] : commit;

        IsDevelopmentBuild = Version.EndsWith("-dev", StringComparison.Ordinal) ||
            Version.Contains("-dev.", StringComparison.Ordinal);
    }
}
