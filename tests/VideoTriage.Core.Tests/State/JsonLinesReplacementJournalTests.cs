using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class JsonLinesReplacementJournalTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _journalPath;
    private readonly JsonLinesReplacementJournal _journal;

    public JsonLinesReplacementJournalTests()
    {
        Directory.CreateDirectory(_tempDir);
        _journalPath = Path.Combine(_tempDir, "replacement-journal.jsonl");
        _journal = new JsonLinesReplacementJournal(_journalPath);
    }

    private static ReplacementTransactionEntry CreateEntry(ReplacementTransactionPhase phase) =>
        new()
        {
            RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            TransactionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Timestamp = DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
            Phase = phase,
            DeleteMode = DeleteMode.Permanent,
            OriginalPath = @"C:\videos\clip.mov",
            OriginalBytes = 1000,
            StagingPath = @"C:\videos\clip.videotriage.staging.mp4",
            IntendedFinalPath = @"C:\videos\clip.mp4",
            ReplacementBytes = 500
        };

    [Theory]
    [InlineData(ReplacementTransactionPhase.Prepared)]
    [InlineData(ReplacementTransactionPhase.OriginalRemoved)]
    [InlineData(ReplacementTransactionPhase.Committed)]
    [InlineData(ReplacementTransactionPhase.Partial)]
    [InlineData(ReplacementTransactionPhase.Recovered)]
    public void AppendThenLoad_RoundTripsPhase(ReplacementTransactionPhase phase)
    {
        var entry = CreateEntry(phase);
        _journal.Append(entry);
        _journal.Load().ShouldContain(entry);
    }

    [Fact]
    public void Load_TruncatedLastLine_ReturnsEarlierCompleteEntries()
    {
        var valid = CreateEntry(ReplacementTransactionPhase.Prepared);
        _journal.Append(valid);
        File.AppendAllText(_journalPath, """{"runId":""");

        var loaded = new JsonLinesReplacementJournal(_journalPath).Load();

        loaded.ShouldBe([valid]);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsEmpty()
    {
        File.WriteAllText(_journalPath, "");
        _journal.Load().ShouldBeEmpty();
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsEmpty()
    {
        new JsonLinesReplacementJournal(Path.Combine(_tempDir, "notexist.jsonl"))
            .Load().ShouldBeEmpty();
    }

    [Fact]
    public void Append_MultipleEntries_AllRoundTrip()
    {
        var entry1 = CreateEntry(ReplacementTransactionPhase.Prepared);
        var entry2 = CreateEntry(ReplacementTransactionPhase.OriginalRemoved) with
        {
            TransactionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
        };
        _journal.Append(entry1);
        _journal.Append(entry2);

        var loaded = _journal.Load();

        loaded.Count.ShouldBe(2);
        loaded[0].ShouldBe(entry1);
        loaded[1].ShouldBe(entry2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
