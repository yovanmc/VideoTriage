using System.IO;
using System.Text.Json;
using VideoTriage.App.Models;

namespace VideoTriage.App.Services;

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            return settings?.SchemaVersion == 2 ? settings : new AppSettings();
        }
        catch (JsonException)
        {
            BackupInvalidFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }

    private void BackupInvalidFile()
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var backupDirectory = string.IsNullOrWhiteSpace(directory)
            ? Environment.CurrentDirectory
            : directory;
        var backup = Path.Combine(
            backupDirectory,
            $"settings.invalid.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Move(path, backup, overwrite: true);
    }
}
