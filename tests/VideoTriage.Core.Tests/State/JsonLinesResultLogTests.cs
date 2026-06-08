using System.Text.Json;
using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class JsonLinesResultLogTests : IDisposable
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly string path = Path.Combine(
        Path.GetTempPath(), "VideoTriage.StateTests", Guid.NewGuid().ToString("N"), "results.jsonl");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Append_RoundTripsEntryWithNullableFields()
    {
        var log = new JsonLinesResultLog(path);
        var entry = new ResultLogEntry
        {
            Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            SourcePath = @"C:\Videos\a.mov",
            Outcome = TriageOutcome.Replaced,
            Message = "Replacement committed.",
            SourceBytes = 1000,
            OutputBytes = 400,
            SavedPercent = 60,
            FinalPath = @"C:\Videos\a.mp4"
        };

        log.Append(entry);

        var line = File.ReadAllLines(path).Single();
        JsonSerializer.Deserialize<ResultLogEntry>(line, Options).ShouldBe(entry);
    }

    [Fact]
    public void Append_NonReplacementOutcome_LeavesSavingsFieldsNull()
    {
        var log = new JsonLinesResultLog(path);
        var entry = new ResultLogEntry
        {
            Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            SourcePath = @"C:\Videos\a.mov",
            Outcome = TriageOutcome.SkippedLowBpp,
            Message = "Low bpp."
        };

        log.Append(entry);

        var loaded = JsonSerializer.Deserialize<ResultLogEntry>(File.ReadAllLines(path).Single(), Options);
        loaded!.OutputBytes.ShouldBeNull();
        loaded.SavedPercent.ShouldBeNull();
        loaded.FinalPath.ShouldBeNull();
    }
}
