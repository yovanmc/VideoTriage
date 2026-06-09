using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Replace;

namespace VideoTriage.Core.Tests.Replace;

public sealed class SafeReplacerTests
{
    // Fixed transaction ID used for deterministic path assertions in tests.
    private static readonly Guid TestTxId = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private const string TestTxN = "00000000000000000000000000000042";

    private static SafeReplacer Build(FakeFileSystem fs, FakeFileRemover remover) =>
        new(fs, remover, () => TestTxId);

    [Fact]
    public void Replace_CandidateNotSmaller_DoesNotRemoveOriginal()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("source.mp4", 1000);
        fs.AddFile("candidate.mp4", 1000); // not smaller
        var remover = new FakeFileRemover(fs);

        var result = Build(fs, remover).Replace("source.mp4", "candidate.mp4", DeleteMode.RecycleBin);

        result.Outcome.ShouldBe(ReplaceOutcome.Failed);
        result.OriginalRemoved.ShouldBeFalse();
        fs.FileExists("source.mp4").ShouldBeTrue();
        fs.Operations.ShouldNotContain(op => op.StartsWith("remove:"));
    }

    [Fact]
    public void Replace_StagingLengthMismatch_DoesNotRemoveOriginal()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("source.mp4", 1000);
        fs.AddFile("candidate.mp4", 500);
        // Simulate the staged file landing at an unexpected size: the guard must refuse to delete.
        fs.StagedLengthOverride[$"source.videotriage.staging.{TestTxN}.mp4"] = 499;
        var remover = new FakeFileRemover(fs);

        var result = Build(fs, remover).Replace("source.mp4", "candidate.mp4", DeleteMode.RecycleBin);

        result.Outcome.ShouldBe(ReplaceOutcome.Failed);
        result.OriginalRemoved.ShouldBeFalse();
        fs.FileExists("source.mp4").ShouldBeTrue();
        fs.Operations.ShouldNotContain(op => op.StartsWith("remove:"));
    }

    [Fact]
    public void Replace_HappyPath_StagesBeforeRemovingOriginal()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("source.mp4", 1000);
        fs.AddFile("candidate.mp4", 500);
        var remover = new FakeFileRemover(fs);

        var result = Build(fs, remover).Replace("source.mp4", "candidate.mp4", DeleteMode.RecycleBin);

        result.Outcome.ShouldBe(ReplaceOutcome.Replaced);
        result.OriginalRemoved.ShouldBeTrue();
        result.FinalPath.ShouldBe("source.mp4");
        fs.Operations.ShouldBe(new[]
        {
            $"move:candidate.mp4->source.videotriage.staging.{TestTxN}.mp4",
            "remove:source.mp4:RecycleBin",
            $"move:source.videotriage.staging.{TestTxN}.mp4->source.mp4"
        });
    }

    [Fact]
    public void Replace_FinalRenameFailure_PreservesPartialAndReturnsReplacePartial()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("source.mp4", 1000);
        fs.AddFile("candidate.mp4", 500);
        fs.FailMoveTo.Add("source.mp4"); // the staging -> final rename fails
        var remover = new FakeFileRemover(fs);

        var result = Build(fs, remover).Replace("source.mp4", "candidate.mp4", DeleteMode.RecycleBin);

        result.Outcome.ShouldBe(ReplaceOutcome.ReplacePartial);
        result.OriginalRemoved.ShouldBeTrue();
        result.FinalPath.ShouldBe($"source.videotriage.partial.{TestTxN}.mp4");
        // Verified bytes were never lost: they live under the partial name.
        fs.FileExists($"source.videotriage.partial.{TestTxN}.mp4").ShouldBeTrue();
        fs.Operations.ShouldBe(new[]
        {
            $"move:candidate.mp4->source.videotriage.staging.{TestTxN}.mp4",
            "remove:source.mp4:RecycleBin",
            $"move:source.videotriage.staging.{TestTxN}.mp4->source.videotriage.partial.{TestTxN}.mp4"
        });
    }

    [Fact]
    public void Replace_DifferentExtensionTargetAlreadyExists_DoesNotRemoveOriginal()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("source.mov", 1000);
        fs.AddFile("candidate.mp4", 500);
        fs.AddFile("source.mp4", 700); // canonical target already exists and differs from original
        var remover = new FakeFileRemover(fs);

        var result = Build(fs, remover).Replace("source.mov", "candidate.mp4", DeleteMode.RecycleBin);

        result.Outcome.ShouldBe(ReplaceOutcome.Failed);
        result.OriginalRemoved.ShouldBeFalse();
        fs.FileExists("source.mov").ShouldBeTrue();
        fs.Operations.ShouldNotContain(op => op.StartsWith("remove:"));
    }

    // REGRESSION (critical): the pipeline encodes to EncodePath(source, txId) and passes that exact
    // path as the candidate. Staging must NOT reuse EncodePath, or the move becomes x -> x and throws.
    [Fact]
    public void Replace_CandidateIsEncodeTempForSameSourceAndTxId_SucceedsWithoutSelfCollision()
    {
        var fs = new FakeFileSystem();
        var encodeTemp = TempFileNaming.EncodePath("source.mp4", TestTxId);
        fs.AddFile("source.mp4", 1000);
        fs.AddFile(encodeTemp, 500);
        var remover = new FakeFileRemover(fs);

        var result = Build(fs, remover).Replace("source.mp4", encodeTemp, DeleteMode.RecycleBin);

        result.Outcome.ShouldBe(ReplaceOutcome.Replaced);
        result.OriginalRemoved.ShouldBeTrue();
        fs.Operations.ShouldBe(new[]
        {
            $"move:source.videotriage.tmp.{TestTxN}.mp4->source.videotriage.staging.{TestTxN}.mp4",
            "remove:source.mp4:RecycleBin",
            $"move:source.videotriage.staging.{TestTxN}.mp4->source.mp4"
        });
        // The encode temp must be consumed (moved into staging), never left behind.
        fs.FileExists(encodeTemp).ShouldBeFalse();
    }

    [Fact]
    public void Replace_PermanentMode_RemovesWithPermanent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("source.mp4", 1000);
        fs.AddFile("candidate.mp4", 500);
        var remover = new FakeFileRemover(fs);

        Build(fs, remover).Replace("source.mp4", "candidate.mp4", DeleteMode.Permanent);

        fs.Operations.ShouldContain("remove:source.mp4:Permanent");
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly Dictionary<string, long> _files = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Operations { get; } = [];
        public HashSet<string> FailMoveTo { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> StagedLengthOverride { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path, long length) => _files[path] = length;
        internal void RemoveInternal(string path) => _files.Remove(path);

        public bool FileExists(string path) => _files.ContainsKey(path);

        public long GetFileLength(string path) =>
            _files.TryGetValue(path, out var length)
                ? length
                : throw new FileNotFoundException("Fake file missing", path);

        public void CreateDirectory(string path) { }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            Operations.Add($"copy:{sourcePath}->{destinationPath}");
            _files[destinationPath] = _files[sourcePath];
        }

        public void MoveFile(string sourcePath, string destinationPath)
        {
            if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Source and destination are the same path: {sourcePath}");
            if (FailMoveTo.Contains(destinationPath))
                throw new IOException($"Simulated move failure to {destinationPath}");

            Operations.Add($"move:{sourcePath}->{destinationPath}");
            var length = _files[sourcePath];
            _files.Remove(sourcePath);
            _files[destinationPath] = StagedLengthOverride.TryGetValue(destinationPath, out var ov) ? ov : length;
        }

        public void DeleteFile(string path)
        {
            Operations.Add($"delete:{path}");
            _files.Remove(path);
        }

        public long GetAvailableFreeSpace(string path) => long.MaxValue;

        public DateTimeOffset GetLastWriteTimeUtc(string path) => DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeFileRemover(FakeFileSystem fileSystem) : IFileRemover
    {
        public void Remove(string path, DeleteMode mode)
        {
            fileSystem.Operations.Add($"remove:{path}:{mode}");
            fileSystem.RemoveInternal(path);
        }
    }
}
