namespace Castorice.Core.Configuration;

/// <summary>
/// Where Castorice keeps its files. Uses the platform config directory so the app stays
/// self-contained on Windows, macOS and Linux alike.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string MappoolDirectory => Path.Combine(Root, "mappools");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(MappoolDirectory);
        HardenPermissions(Root);
    }

    /// <summary>
    /// The settings file holds an IRC password and an API secret, so on Unix the config directory
    /// is restricted to the owner. Windows relies on the per-user profile ACL.
    /// </summary>
    public static void HardenPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = Directory.Exists(path)
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite;

            File.SetUnixFileMode(path, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A file system that cannot express the mode is not a reason to fail startup.
        }
    }

    private static string ResolveRoot()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrEmpty(appData))
        {
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(appData, "Castorice");
    }
}
