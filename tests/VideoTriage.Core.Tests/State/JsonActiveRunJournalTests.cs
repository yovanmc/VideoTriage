using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class JsonActiveRunJournalTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly JsonActiveRunJournal _journal;

    public JsonActiveRunJournalTests()
    {
        Directory.CreateDirectory(_tempDir);
        _journal = new JsonActiveRunJournal(_tempDir);
    }

    private static ActiveRunState SampleState() => new()
    {
        RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Folder = @"C:\videos",
        StartedAtUtc = DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
        CurrentFile = @"C:\videos\clip.mov",
        CurrentPhase = TriagePhase.Encoding,
        CompletedFiles = 3,
        TotalFiles = 10
    };

    [Fact]
    public void SaveThenLoad_RoundTripsCurrentFileAndPhase()
    {
        var state = SampleState();
        _journal.Save(state);
        _journal.Load().ShouldBe(state);
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsNull()
    {
        _journal.Load().ShouldBeNull();
    }

    [Fact]
    public void Clear_AfterSave_LoadReturnsNull()
    {
        _journal.Save(SampleState());
        _journal.Clear();
        _journal.Load().ShouldBeNull();
    }

    [Fact]
    public void Save_OverwritesPreviousState()
    {
        var state1 = SampleState();
        var state2 = state1 with { CompletedFiles = 7, CurrentFile = @"C:\videos\other.mov" };
        _journal.Save(state1);
        _journal.Save(state2);
        _journal.Load().ShouldBe(state2);
    }

    [Fact]
    public void Clear_NonExistentFile_DoesNotThrow()
    {
        Should.NotThrow(() => _journal.Clear());
    }

    [Fact]
    public void Save_CorruptedExistingFile_OverwritesSuccessfully()
    {
        var path = Path.Combine(_tempDir, "active-run.json");
        File.WriteAllText(path, "{ corrupted json {{{");
        var state = SampleState();
        _journal.Save(state);
        _journal.Load().ShouldBe(state);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
