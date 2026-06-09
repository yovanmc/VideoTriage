using System.Text.Json;

namespace VideoTriage.Core.State;

/// <summary>
/// Atomically persists <see cref="ActiveRunState"/> to <c>active-run.json</c> in the given
/// directory. Uses a temp-file swap so a mid-write crash never leaves a partially written file.
/// </summary>
public sealed class JsonActiveRunJournal(string dataDirectory) : IActiveRunJournal
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private string FinalPath => Path.Combine(dataDirectory, "active-run.json");
    private string TempPath  => Path.Combine(dataDirectory, "active-run.json.tmp");

    public void Save(ActiveRunState state)
    {
        Directory.CreateDirectory(dataDirectory);

        using (var fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, state, Options);
            fs.Flush(flushToDisk: true);
        }

        File.Move(TempPath, FinalPath, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(FinalPath)) File.Delete(FinalPath);
        if (File.Exists(TempPath))  File.Delete(TempPath);
    }

    public ActiveRunState? Load()
    {
        if (!File.Exists(FinalPath))
            return null;

        try
        {
            using var fs = File.OpenRead(FinalPath);
            return JsonSerializer.Deserialize<ActiveRunState>(fs, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
