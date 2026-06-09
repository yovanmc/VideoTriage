using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class ReplacementRecoveryTests
{
    private static readonly Guid RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TxId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static ReplacementTransactionEntry Entry(
        ReplacementTransactionPhase phase,
        DateTimeOffset? timestamp = null) => new()
    {
        RunId = RunId,
        TransactionId = TxId,
        Timestamp = timestamp ?? DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
        Phase = phase,
        DeleteMode = DeleteMode.Permanent,
        OriginalPath = @"C:\videos\clip.mov",
        OriginalBytes = 1000,
        StagingPath = @"C:\videos\clip.videotriage.staging.mp4",
        IntendedFinalPath = @"C:\videos\clip.mp4",
        ReplacementBytes = 500
    };

    [Fact]
    public void Recover_PreparedOriginalAndStagingExist_DeletesStagingAndJournalsRecovered()
    {
        var fakes = new RecoveryFakes([Entry(ReplacementTransactionPhase.Prepared)]);
        fakes.ExistingFiles.Add(@"C:\videos\clip.mov");
        fakes.ExistingFiles.Add(@"C:\videos\clip.videotriage.staging.mp4");

        var report = fakes.Recovery.Recover();

        report.Cleaned.ShouldBe([@"C:\videos\clip.mov"]);
        report.Recovered.ShouldBeEmpty();
        report.Unrecoverable.ShouldBeEmpty();
        fakes.Deletes.ShouldContain(@"C:\videos\clip.videotriage.staging.mp4");
        fakes.JournalPhases.ShouldContain(ReplacementTransactionPhase.Recovered);
        fakes.ManifestAppends.ShouldBe(0);
    }

    [Fact]
    public void Recover_PreparedOriginalMissingStagingExists_MovesToFinalAndRepairsManifest()
    {
        var fakes = new RecoveryFakes([Entry(ReplacementTransactionPhase.Prepared)]);
        fakes.ExistingFiles.Add(@"C:\videos\clip.videotriage.staging.mp4");
        // original missing, final path not taken

        var report = fakes.Recovery.Recover();

        report.Recovered.ShouldBe([@"C:\videos\clip.mov"]);
        fakes.Moves.ShouldContain(
            (@"C:\videos\clip.videotriage.staging.mp4", @"C:\videos\clip.mp4"));
        fakes.JournalPhases.ShouldContain(ReplacementTransactionPhase.OriginalRemoved);
        fakes.JournalPhases.ShouldContain(ReplacementTransactionPhase.Recovered);
        fakes.ManifestAppends.ShouldBe(1);
    }

    [Fact]
    public void Recover_OriginalRemoved_StagingExists_MovesToFinalAndRepairsManifest()
    {
        var fakes = new RecoveryFakes([
            Entry(ReplacementTransactionPhase.Prepared),
            Entry(ReplacementTransactionPhase.OriginalRemoved,
                  DateTimeOffset.Parse("2026-06-09T12:00:01Z"))
        ]);
        fakes.ExistingFiles.Add(@"C:\videos\clip.videotriage.staging.mp4");

        var report = fakes.Recovery.Recover();

        report.Recovered.ShouldBe([@"C:\videos\clip.mov"]);
        fakes.Moves.ShouldContain(
            (@"C:\videos\clip.videotriage.staging.mp4", @"C:\videos\clip.mp4"));
        fakes.JournalPhases.ShouldContain(ReplacementTransactionPhase.Recovered);
        fakes.ManifestAppends.ShouldBe(1);
    }

    [Fact]
    public void Recover_Committed_NoMutation()
    {
        var fakes = new RecoveryFakes([
            Entry(ReplacementTransactionPhase.Prepared),
            Entry(ReplacementTransactionPhase.OriginalRemoved,
                  DateTimeOffset.Parse("2026-06-09T12:00:01Z")),
            Entry(ReplacementTransactionPhase.Committed,
                  DateTimeOffset.Parse("2026-06-09T12:00:02Z"))
        ]);

        var report = fakes.Recovery.Recover();

        report.Recovered.ShouldBeEmpty();
        report.Cleaned.ShouldBeEmpty();
        report.Unrecoverable.ShouldBeEmpty();
        fakes.Deletes.ShouldBeEmpty();
        fakes.Moves.ShouldBeEmpty();
    }

    [Fact]
    public void Recover_PreparedBothFilesMissing_ReturnsUnrecoverable()
    {
        var fakes = new RecoveryFakes([Entry(ReplacementTransactionPhase.Prepared)]);
        // No files in ExistingFiles — both missing

        var report = fakes.Recovery.Recover();

        report.Unrecoverable.ShouldNotBeEmpty();
        report.Unrecoverable.Single().OriginalPath.ShouldBe(@"C:\videos\clip.mov");
        report.Recovered.ShouldBeEmpty();
        fakes.Moves.ShouldBeEmpty();
    }

    [Fact]
    public void Recover_EmptyJournal_ReturnsAllEmpty()
    {
        var fakes = new RecoveryFakes([]);

        var report = fakes.Recovery.Recover();

        report.Recovered.ShouldBeEmpty();
        report.Cleaned.ShouldBeEmpty();
        report.Unrecoverable.ShouldBeEmpty();
    }

    [Fact]
    public void Recover_MultipleTransactions_EachHandledIndependently()
    {
        var tx2 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var fakes = new RecoveryFakes([
            Entry(ReplacementTransactionPhase.Prepared),
            new ReplacementTransactionEntry
            {
                RunId = RunId,
                TransactionId = tx2,
                Timestamp = DateTimeOffset.Parse("2026-06-09T13:00:00Z"),
                Phase = ReplacementTransactionPhase.Committed,
                DeleteMode = DeleteMode.Permanent,
                OriginalPath = @"C:\videos\other.mov",
                OriginalBytes = 2000,
                StagingPath = @"C:\videos\other.videotriage.staging.mp4",
                IntendedFinalPath = @"C:\videos\other.mp4",
                ReplacementBytes = 800
            }
        ]);
        fakes.ExistingFiles.Add(@"C:\videos\clip.mov");
        fakes.ExistingFiles.Add(@"C:\videos\clip.videotriage.staging.mp4");

        var report = fakes.Recovery.Recover();

        // tx1: Prepared + both files → cleaned
        report.Cleaned.ShouldContain(@"C:\videos\clip.mov");
        // tx2: Committed → no mutation
        report.Recovered.ShouldBeEmpty();
        report.Unrecoverable.ShouldBeEmpty();
    }

    // --- Fakes ---

    private sealed class RecoveryFakes
    {
        public HashSet<string> ExistingFiles { get; } = [];
        public List<ReplacementTransactionPhase> JournalPhases { get; } = [];
        public List<(string From, string To)> Moves { get; } = [];
        public List<string> Deletes { get; } = [];
        public int ManifestAppends { get; private set; }

        private readonly List<ReplacementTransactionEntry> _journalEntries;
        public IReplacementRecovery Recovery { get; }

        public RecoveryFakes(IReadOnlyList<ReplacementTransactionEntry> entries)
        {
            _journalEntries = [.. entries];
            Recovery = new ReplacementRecovery(
                new FakeJournal(this, _journalEntries),
                new FakeFileSystem(this),
                new FakeManifest(this));
        }

        private sealed class FakeJournal(RecoveryFakes f, List<ReplacementTransactionEntry> entries) : IReplacementJournal
        {
            public void Append(ReplacementTransactionEntry entry) => f.JournalPhases.Add(entry.Phase);
            public IReadOnlyList<ReplacementTransactionEntry> Load() => entries;
        }

        private sealed class FakeFileSystem(RecoveryFakes f) : IFileSystem
        {
            public bool FileExists(string path) => f.ExistingFiles.Contains(path);
            public long GetFileLength(string path) => 500;
            public void CreateDirectory(string path) { }
            public void CopyFile(string src, string dst, bool overwrite) { }
            public void MoveFile(string src, string dst) => f.Moves.Add((src, dst));
            public void DeleteFile(string path) => f.Deletes.Add(path);
            public long GetAvailableFreeSpace(string path) => long.MaxValue;
            public DateTimeOffset GetLastWriteTimeUtc(string path) => DateTimeOffset.UtcNow;
        }

        private sealed class FakeManifest(RecoveryFakes f) : IDeleteManifest
        {
            public void Append(DeleteManifestEntry entry) => f.ManifestAppends++;
        }
    }
}
