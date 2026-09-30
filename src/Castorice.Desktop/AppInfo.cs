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
    /// The version the build was stamped with, e.g. <c>0.1.0</c>. The SDK appends the commit as
    /// <c>+sha</c>; that part is left off.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
