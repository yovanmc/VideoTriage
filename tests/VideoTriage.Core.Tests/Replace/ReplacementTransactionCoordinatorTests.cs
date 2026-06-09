using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.Replace;

public sealed class ReplacementTransactionCoordinatorTests
{
    private static readonly Guid RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TxId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string OriginalPath = @"C:\videos\clip.mov";
    private const string ReplacementPath = @"C:\videos\clip.videotriage.tmp.mp4";
    private const string StagingPath = @"C:\videos\clip.videotriage.staging.mp4";
    private const string FinalPath = @"C:\videos\clip.mp4";

    private static ReplacementTransactionRequest MakeRequest(
        string original = OriginalPath,
        string replacement = ReplacementPath) => new()
    {
        RunId = RunId,
        OriginalPath = original,
        VerifiedReplacementPath = replacement,
        OriginalBytes = 1000,
        ReplacementBytes = 500,
        DeleteMode = DeleteMode.Permanent
    };

    [Fact]
    public void Replace_Success_JournalsAllPhasesInOrder()
    {
        var fakes = new CoordinatorFakes();
        var result = fakes.Coordinator.Replace(MakeRequest());

        result.Outcome.ShouldBe(ReplaceOutcome.Replaced);
        result.OriginalRemoved.ShouldBeTrue();
        result.FinalPath.ShouldBe(FinalPath);

        fakes.JournalPhases.ShouldBe([
            ReplacementTransactionPhase.Prepared,
            ReplacementTransactionPhase.OriginalRemoved,
            ReplacementTransactionPhase.Committed
        ]);
        // move: replacement→staging, then staging→final
        fakes.Moves.ShouldBe([(ReplacementPath, StagingPath), (StagingPath, FinalPath)]);
        fakes.Removes.ShouldBe([OriginalPath]);
        fakes.ManifestAppends.ShouldBe(1);
    }

    [Fact]
    public void Replace_RemoveThrows_DeletesStagingAndReturnsFailedWithoutJournalingRemoved()
    {
        var fakes = new CoordinatorFakes();
        fakes.ThrowOnRemove = true;

        var result = fakes.Coordinator.Replace(MakeRequest());

        result.Outcome.ShouldBe(ReplaceOutcome.Failed);
        result.OriginalRemoved.ShouldBeFalse();

        fakes.JournalPhases.ShouldBe([ReplacementTransactionPhase.Prepared]);
        // Staging must be deleted after the remove failure (original is still alive)
        fakes.Deletes.ShouldContain(StagingPath);
        fakes.ManifestAppends.ShouldBe(0);
    }

    [Fact]
    public void Replace_FinalMoveThrows_JournalsPartialAndPreservesStaging()
    {
        var fakes = new CoordinatorFakes();
        fakes.ThrowOnMoveToFinal = true;

        var result = fakes.Coordinator.Replace(MakeRequest());

        result.Outcome.ShouldBe(ReplaceOutcome.ReplacePartial);
        result.OriginalRemoved.ShouldBeTrue();
        // Staging path is the preserved partial (since final rename failed)
        result.FinalPath.ShouldBe(StagingPath);

        fakes.JournalPhases.ShouldBe([
            ReplacementTransactionPhase.Prepared,
            ReplacementTransactionPhase.OriginalRemoved,
            ReplacementTransactionPhase.Partial
        ]);
        // Staging must NOT be deleted
        fakes.Deletes.ShouldNotContain(StagingPath);
        fakes.ManifestAppends.ShouldBe(1);
    }

    [Fact]
    public void Replace_ManifestThrows_ReturnsReplacePartialButJournalStillShowsCommitted()
    {
        var fakes = new CoordinatorFakes();
        fakes.ThrowOnManifest = true;

        var result = fakes.Coordinator.Replace(MakeRequest());

        // manifest failure after successful commit => ReplacePartial
        result.Outcome.ShouldBe(ReplaceOutcome.ReplacePartial);
        result.OriginalRemoved.ShouldBeTrue();
        result.FinalPath.ShouldBe(FinalPath);

        // Journal still shows Committed (the journal proves the bytes are safe)
        fakes.JournalPhases.ShouldBe([
            ReplacementTransactionPhase.Prepared,
            ReplacementTransactionPhase.OriginalRemoved,
            ReplacementTransactionPhase.Committed
        ]);
    }

    [Fact]
    public void Replace_OriginalMissing_ReturnsFailedWithoutAnyMutation()
    {
        var fakes = new CoordinatorFakes();
        fakes.MissingFiles.Add(OriginalPath);

        var result = fakes.Coordinator.Replace(MakeRequest());

        result.Outcome.ShouldBe(ReplaceOutcome.Failed);
        fakes.JournalPhases.ShouldBeEmpty();
        fakes.Moves.ShouldBeEmpty();
    }

    // --- Fakes ---

    private sealed class CoordinatorFakes
    {
        public bool ThrowOnRemove { get; set; }
        public bool ThrowOnMoveToFinal { get; set; }
        public bool ThrowOnManifest { get; set; }
        public HashSet<string> MissingFiles { get; } = [];
        public List<ReplacementTransactionPhase> JournalPhases { get; } = [];
        public List<(string From, string To)> Moves { get; } = [];
        public List<string> Deletes { get; } = [];
        public List<string> Removes { get; } = [];
        public int ManifestAppends { get; private set; }

        public IReplacementTransactionCoordinator Coordinator { get; }

        public CoordinatorFakes()
        {
            Coordinator = new ReplacementTransactionCoordinator(
                new FakeJournal(this),
                new FakeFileSystem(this),
                new FakeRemover(this),
                new FakeManifest(this),
                () => TxId); // deterministic transaction ID for assertions
        }

        private sealed class FakeJournal(CoordinatorFakes f) : IReplacementJournal
        {
            public void Append(ReplacementTransactionEntry entry) => f.JournalPhases.Add(entry.Phase);
            public IReadOnlyList<ReplacementTransactionEntry> Load() => [];
        }

        private sealed class FakeFileSystem(CoordinatorFakes f) : IFileSystem
        {
            public bool FileExists(string path) => !f.MissingFiles.Contains(path);
            public long GetFileLength(string path) => path.Contains(".staging.") ? 500 : 1000;
            public void CreateDirectory(string path) { }
            public void CopyFile(string src, string dst, bool overwrite) { }
            public void MoveFile(string src, string dst)
            {
                if (f.ThrowOnMoveToFinal && dst.EndsWith(".mp4") && !dst.Contains(".staging.") && !dst.Contains(".tmp."))
                    throw new IOException("Access denied.");
                f.Moves.Add((src, dst));
            }
            public void DeleteFile(string path) => f.Deletes.Add(path);
            public long GetAvailableFreeSpace(string path) => long.MaxValue;
            public DateTimeOffset GetLastWriteTimeUtc(string path) => DateTimeOffset.UtcNow;
        }

        private sealed class FakeRemover(CoordinatorFakes f) : IFileRemover
        {
            public void Remove(string path, DeleteMode mode)
            {
                if (f.ThrowOnRemove) throw new IOException("File in use.");
                f.Removes.Add(path);
            }
        }

        private sealed class FakeManifest(CoordinatorFakes f) : IDeleteManifest
        {
            public void Append(DeleteManifestEntry entry)
            {
                if (f.ThrowOnManifest) throw new IOException("Manifest write failed.");
                f.ManifestAppends++;
            }
        }
    }
}
