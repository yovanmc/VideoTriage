using System.Text.Json;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

/// <summary>
/// Durable JSONL replacement journal. Each append is written with WriteThrough and flushed to disk.
/// Load skips truncated or malformed last lines (partial writes on crash).
/// </summary>
public sealed class JsonLinesReplacementJournal : IReplacementJournal
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly object _lock = new();

    public JsonLinesReplacementJournal(string path) => _path = path;

    public void Append(ReplacementTransactionEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, _jsonOptions) + Environment.NewLine;
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = new FileStream(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }

    public IReadOnlyList<ReplacementTransactionEntry> Load()
    {
        if (!File.Exists(_path))
            return [];

        var results = new List<ReplacementTransactionEntry>();
        foreach (var line in File.ReadLines(_path, System.Text.Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var entry = JsonSerializer.Deserialize<ReplacementTransactionEntry>(line, _jsonOptions);
                if (entry is not null)
                    results.Add(entry);
            }
            catch (JsonException)
            {
                // Truncated or malformed line — skip it (crash mid-write is expected)
            }
        }
        return results;
    }
}
