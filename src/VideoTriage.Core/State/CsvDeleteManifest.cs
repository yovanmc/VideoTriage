using System.Globalization;
using System.Text;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

/// <summary>
/// Append-only CSV audit of removed originals. The header is written once; every field is quoted and
/// internal quotes are doubled so paths containing commas or quotes round-trip safely.
/// </summary>
public sealed class CsvDeleteManifest(string path) : IDeleteManifest
{
    private const string Header =
        "Timestamp,DeleteMode,OriginalPath,OriginalBytes,ReplacementPath,ReplacementBytes,SavedPercent";

    public void Append(DeleteManifestEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        var builder = new StringBuilder();
        if (needsHeader) builder.AppendLine(Header);

        builder.AppendLine(string.Join(',',
            Csv(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            Csv(entry.DeleteMode.ToString()),
            Csv(entry.OriginalPath),
            Csv(entry.OriginalBytes.ToString(CultureInfo.InvariantCulture)),
            Csv(entry.ReplacementPath),
            Csv(entry.ReplacementBytes.ToString(CultureInfo.InvariantCulture)),
            Csv(entry.SavedPercent.ToString(CultureInfo.InvariantCulture))));

        File.AppendAllText(path, builder.ToString());
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
