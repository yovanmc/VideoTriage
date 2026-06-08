using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class JsonLinesCompletedFileStoreTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(), "VideoTriage.StateTests", Guid.NewGuid().ToString("N"), "done.jsonl");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void AppendThenLoad_RoundTripsCompleteEntry()
    {
        var store = new JsonLinesCompletedFileStore(path);
        var entry = Entry(@"C:\Videos\a.mp4", 10, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        store.Append(entry);

        store.Load().ShouldBe([entry]);
    }

    [Fact]
    public void Append_MultipleEntries_LoadsAllInOrder()
    {
        var store = new JsonLinesCompletedFileStore(path);
        var a = Entry(@"C:\Videos\a.mp4", 10, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var b = Entry(@"C:\Videos\b.mp4", 20, DateTimeOffset.Parse("2026-01-03T00:00:00Z"));

        store.Append(a);
        store.Append(b);

        store.Load().ShouldBe([a, b]);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty() =>
        new JsonLinesCompletedFileStore(path).Load().ShouldBeEmpty();

    [Fact]
    public void Load_MalformedLine_IgnoresLineAndContinues()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path,
        [
            "{broken",
            System.Text.Json.JsonSerializer.Serialize(
                Entry(@"C:\a.mp4", 1, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        ]);

        new JsonLinesCompletedFileStore(path).Load().Count.ShouldBe(1);
    }

    private static CompletedFileEntry Entry(string path, long length, DateTimeOffset lastWrite) => new()
    {
        SourcePath = path,
        SourceLength = length,
        SourceLastWriteUtc = lastWrite,
        Outcome = TriageOutcome.Replaced,
        CompletedAtUtc = DateTimeOffset.Parse("2026-01-02T00:00:00Z")
    };
}
