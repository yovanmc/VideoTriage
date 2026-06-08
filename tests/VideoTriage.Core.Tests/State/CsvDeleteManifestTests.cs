using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class CsvDeleteManifestTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(), "VideoTriage.StateTests", Guid.NewGuid().ToString("N"), "deletions.csv");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Append_WritesHeaderOnceAndQuotesPaths()
    {
        var manifest = new CsvDeleteManifest(path);

        manifest.Append(new DeleteManifestEntry
        {
            Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DeleteMode = DeleteMode.RecycleBin,
            OriginalPath = @"C:\Videos\a, ""quote"".mov",
            OriginalBytes = 100,
            ReplacementPath = @"C:\Videos\a.mp4",
            ReplacementBytes = 40,
            SavedPercent = 60
        });
        manifest.Append(new DeleteManifestEntry
        {
            Timestamp = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            DeleteMode = DeleteMode.Permanent,
            OriginalPath = @"C:\Videos\b.mov",
            OriginalBytes = 100,
            ReplacementPath = @"C:\Videos\b.mp4",
            ReplacementBytes = 50,
            SavedPercent = 50
        });

        var lines = File.ReadAllLines(path);
        lines[0].ShouldBe("Timestamp,DeleteMode,OriginalPath,OriginalBytes,ReplacementPath,ReplacementBytes,SavedPercent");
        lines.Length.ShouldBe(3);
        lines[1].ShouldContain(@"""C:\Videos\a, """"quote"""".mov""");
        lines[1].ShouldContain("RecycleBin");
        lines[2].ShouldContain("Permanent");
    }

    [Fact]
    public void Append_ExistingFile_DoesNotRewriteHeader()
    {
        var manifest = new CsvDeleteManifest(path);
        DeleteManifestEntry Entry(string p) => new()
        {
            Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DeleteMode = DeleteMode.RecycleBin,
            OriginalPath = p,
            OriginalBytes = 100,
            ReplacementPath = @"C:\Videos\x.mp4",
            ReplacementBytes = 40,
            SavedPercent = 60
        };

        manifest.Append(Entry(@"C:\Videos\a.mov"));
        new CsvDeleteManifest(path).Append(Entry(@"C:\Videos\b.mov")); // new instance, same file

        var lines = File.ReadAllLines(path);
        lines.Count(l => l.StartsWith("Timestamp,")).ShouldBe(1);
        lines.Length.ShouldBe(3);
    }
}
