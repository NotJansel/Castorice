using System.Diagnostics;
using System.Runtime.InteropServices;
using Castorice.Core.Updates;

namespace Castorice.Desktop.Services;

/// <summary>How the running copy was installed, which decides how an update can be put in place.</summary>
public enum UpdateInstallMode
{
    /// <summary>Not installed in a way Castorice can update itself: the release page is opened.</summary>
    Manual,

    /// <summary>Installed with the Windows setup program, which can install over itself.</summary>
    WindowsInstaller,

    /// <summary>Running from Castorice.app, which can be swapped for the one in the new disk image.</summary>
    MacBundle,

    /// <summary>Running as an AppImage, whose file can be replaced by the new one.</summary>
    AppImage,
}

/// <summary>Puts a downloaded update in place, the way that fits how Castorice was installed.</summary>
public static class UpdateInstaller
{
    /// <summary>Waits for Castorice to quit, swaps the app bundle for the new one and starts it.</summary>
    private const string MacSwapScript = """
        #!/bin/sh
        # Castorice update: $1 = pid to wait for, $2 = disk image, $3 = app bundle to replace.
        pid="$1"; dmg="$2"; target="$3"
        exec >>"$HOME/Library/Logs/Castorice-update.log" 2>&1
        echo "$(date): updating $target from $dmg"

        while kill -0 "$pid" 2>/dev/null; do sleep 0.5; done

        mnt="$(mktemp -d)"
        if ! hdiutil attach -nobrowse -readonly -mountpoint "$mnt" "$dmg"; then
          echo "could not mount the disk image"; open "$dmg"; exit 1
        fi

        staged="$target.update"
        rm -rf "$staged"
        if ditto "$mnt/Castorice.app" "$staged" && rm -rf "$target" && mv "$staged" "$target"; then
          hdiutil detach "$mnt" -quiet
          rm -f "$dmg"
          echo "done"
          open "$target"
        else
          echo "could not replace the app; opening the disk image instead"
          rm -rf "$staged"
          hdiutil detach "$mnt" -quiet
          open "$dmg"
          exit 1
        fi
        """;

    public static UpdatePlatform Platform { get; } = DetectPlatform();

    public static UpdateInstallMode Mode { get; } = DetectMode();

    /// <summary>The app bundle the running copy lives in, when it lives in one.</summary>
    private static string? MacBundlePath
    {
        get
        {
            var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var suffix = Path.Combine(".app", "Contents", "MacOS");

            return directory.EndsWith(suffix, StringComparison.Ordinal)
                ? Path.GetFullPath(Path.Combine(directory, "..", ".."))
                : null;
        }
    }

    /// <summary>
    /// Whether an update can be put in place from inside Castorice. An AppImage needs to be able
    /// to write to its folder; otherwise the release page is the way.
    /// </summary>
    public static bool CanInstallInPlace => Mode switch
    {
        UpdateInstallMode.WindowsInstaller => true,
        UpdateInstallMode.MacBundle => MacBundlePath is not null,
        UpdateInstallMode.AppImage => AppImagePath is { } appImage && CanWriteNextTo(appImage),
        _ => false,
    };

    private static string? AppImagePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } path && File.Exists(path) ? path : null;

    /// <summary>
    /// Where to download the update to. An AppImage is downloaded next to itself, so the swap is
    /// a rename on the same disk; everything else goes to the temp folder.
    /// </summary>
    public static string DownloadPathFor(UpdateAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (Mode is UpdateInstallMode.AppImage && AppImagePath is { } appImage)
        {
            return Path.Combine(Path.GetDirectoryName(appImage)!, $".{asset.Name}.download");
        }

        var directory = Path.Combine(Path.GetTempPath(), "castorice-update");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, asset.Name);
    }

    /// <summary>
    /// Hands the downloaded file over. Returns true when Castorice should now quit so the update
    /// can finish; false when it only opened something for the user to finish by hand.
    /// </summary>
    public static bool Apply(string downloadedFile, UpdateInfo update, out string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadedFile);
        ArgumentNullException.ThrowIfNull(update);

        switch (Mode)
        {
            case UpdateInstallMode.WindowsInstaller:
                // Silent, over the existing installation: the setup keeps the install location and
                // whether it was for this user or everyone. /CASTORICEUPDATE makes it start
                // Castorice again when done, as the user rather than elevated.
                Process.Start(new ProcessStartInfo(downloadedFile)
                {
                    UseShellExecute = true,
                    Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /CASTORICEUPDATE=1",
                });
                message = $"Installing Castorice {update.Version}…";
                return true;

            case UpdateInstallMode.MacBundle when MacBundlePath is { } bundle:
                if (!CanWriteNextTo(bundle))
                {
                    OpenFile(downloadedFile);
                    message = $"Castorice cannot replace itself in {Path.GetDirectoryName(bundle)}. " +
                        "The new version's disk image is open: quit Castorice and drag it into Applications.";
                    return false;
                }

                var script = Path.Combine(Path.GetDirectoryName(downloadedFile)!, "castorice-update.sh");
                File.WriteAllText(script, MacSwapScript.Replace("\r\n", "\n", StringComparison.Ordinal));

                var swap = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                swap.ArgumentList.Add(script);
                swap.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                swap.ArgumentList.Add(downloadedFile);
                swap.ArgumentList.Add(bundle);
                Process.Start(swap);

                message = $"Castorice restarts as {update.Version} in a moment.";
                return true;

            case UpdateInstallMode.AppImage when AppImagePath is { } appImage:
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        downloadedFile,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                // A rename, so the running copy keeps its file until it exits. The new one starts
                // only once this one is gone, so the two never log in to Bancho at the same time.
                File.Move(downloadedFile, appImage, overwrite: true);

                var relaunch = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                relaunch.ArgumentList.Add("-c");
                relaunch.ArgumentList.Add("while kill -0 \"$1\" 2>/dev/null; do sleep 0.5; done; exec \"$2\"");
                relaunch.ArgumentList.Add("castorice-update");
                relaunch.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                relaunch.ArgumentList.Add(appImage);
                Process.Start(relaunch);

                message = $"Starting Castorice {update.Version}…";
                return true;

            default:
                OpenUrl(update.PageUrl);
                message = $"Castorice {update.Version} is on its release page, which is now open.";
                return false;
        }
    }

    /// <summary>Opens a web page in the default browser. Best effort.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Could not open {url}: {ex.Message}");
        }
    }

    /// <summary>Opens a file with its default app — a disk image mounts in Finder. Best effort.</summary>
    private static void OpenFile(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var open = new ProcessStartInfo("open");
                open.ArgumentList.Add(path);
                Process.Start(open);
            }
            else
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Could not open {path}: {ex.Message}");
        }
    }

    private static bool CanWriteNextTo(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return false;
        }

        try
        {
            var probe = Path.Combine(directory, $".castorice-write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static UpdateInstallMode DetectMode()
    {
        if (OperatingSystem.IsWindows())
        {
            // The setup program leaves its uninstaller next to the app; the portable zip does not.
            return File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"))
                ? UpdateInstallMode.WindowsInstaller
                : UpdateInstallMode.Manual;
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacBundlePath is not null ? UpdateInstallMode.MacBundle : UpdateInstallMode.Manual;
        }

        if (OperatingSystem.IsLinux())
        {
            return AppImagePath is not null ? UpdateInstallMode.AppImage : UpdateInstallMode.Manual;
        }

        return UpdateInstallMode.Manual;
    }

    private static UpdatePlatform DetectPlatform()
    {
        var arm = RuntimeInformation.ProcessArchitecture is Architecture.Arm64;

        if (OperatingSystem.IsWindows())
        {
            return arm ? UpdatePlatform.WindowsArm64 : UpdatePlatform.WindowsX64;
        }

        if (OperatingSystem.IsMacOS())
        {
            return arm ? UpdatePlatform.MacArm64 : UpdatePlatform.MacX64;
        }

        if (OperatingSystem.IsLinux())
        {
            return arm ? UpdatePlatform.LinuxArm64 : UpdatePlatform.LinuxX64;
        }

        return UpdatePlatform.Unknown;
    }
}
