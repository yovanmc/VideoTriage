using System.Text.Json;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

/// <summary>
/// Append-only JSON Lines store of completed-file identities. One complete record per line; a
/// malformed line is skipped rather than aborting a resume.
/// </summary>
public sealed class JsonLinesCompletedFileStore(string path) : ICompletedFileStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<CompletedFileEntry> Load()
    {
        if (!File.Exists(path)) return [];

        var entries = new List<CompletedFileEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<CompletedFileEntry>(line, Options);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.SourcePath))
                    entries.Add(entry);
            }
            catch (JsonException)
            {
                // Skip malformed lines so a partially written record can never block resume.
            }
        }

        return entries;
    }

    public void Append(CompletedFileEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
    }
}
