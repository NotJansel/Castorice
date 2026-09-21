using System.Text.Json;

namespace Castorice.Core.Configuration;

public sealed class SettingsStore(string? path = null)
{
    private readonly string _path = path ?? AppPaths.SettingsFile;

    public string Path => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, CastoriceJson.Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt settings file must not stop the app from starting.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            AppPaths.HardenPermissions(directory);
        }

        var json = JsonSerializer.Serialize(settings, CastoriceJson.Options);

        // Write through a temp file so an interrupted save cannot truncate the existing settings.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        AppPaths.HardenPermissions(temp);
        File.Move(temp, _path, overwrite: true);
        AppPaths.HardenPermissions(_path);
    }
}
