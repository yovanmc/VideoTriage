using System.Text.Json;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

/// <summary>Append-only JSON Lines log of every terminal per-file result.</summary>
public sealed class JsonLinesResultLog(string path) : IResultLog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public void Append(ResultLogEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
    }
}
