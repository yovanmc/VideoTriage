using Shouldly;
using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Pipeline;

public sealed class TriagePipelineTests
{
    [Fact]
    public async Task RunAsync_LowBpp_SkipsWithoutEncoding()
    {
        var fakes = PipelineFakes.LowBpp();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Skipped.ShouldBe(1);
        fakes.Calls.ShouldBe(["probe", "classify"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_ProbeFailure_CountsInvalid()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.ProbeSucceeds = false;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Invalid.ShouldBe(1);
        fakes.Calls.ShouldBe(["probe"]);
    }

    [Fact]
    public async Task RunAsync_DryRun_StopsAfterClassification()
    {
        var fakes = PipelineFakes.Candidate();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions { DryRun = true });

        result.Skipped.ShouldBe(1);
        fakes.Calls.ShouldBe(["probe", "classify"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_InsufficientSpace_KeepsOriginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.AvailableBytes = 10;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Failed.ShouldBe(1);
        fakes.Calls.ShouldNotContain("replace");
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_EncodeFailure_KeepsOriginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.EncodeOutcome = EncodeOutcome.Failed;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Failed.ShouldBe(1);
        fakes.Calls.ShouldBe(["probe", "classify", "space", "encode"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_VerificationFailure_KeepsOriginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.Verification = new VerificationResult
        {
            Outcome = VerificationOutcome.DecodeError,
            Reason = "corrupt"
        };

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Invalid.ShouldBe(1);
        fakes.Calls.ShouldBe(["probe", "classify", "space", "encode", "verify", "delete-temp"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_OutputNotSmaller_KeepsOriginalAndCountsGrew()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.OutputBytes = 1000; // equal to source => not smaller

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Grew.ShouldBe(1);
        fakes.Calls.ShouldBe(["probe", "classify", "space", "encode", "verify", "delete-temp"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_SmallerVerifiedOutput_CallsSafeReplacer()
    {
        var fakes = PipelineFakes.Candidate();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Replaced.ShouldBe(1);
        fakes.Calls.ShouldBe(["probe", "classify", "space", "encode", "verify", "replace"]);
        fakes.OriginalRemoved.ShouldBeTrue();
        // C3: savings must be computed, never left at defaults. Source 1000, output 500 => 500 bytes, 50%.
        result.BytesSaved.ShouldBe(500);
        var file = result.Files.Single();
        file.OutputBytes.ShouldBe(500);
        file.SavedPercent!.Value.ShouldBe(50, 0.01);
    }

    [Fact]
    public async Task RunAsync_SmallSavingUnderThreshold_CountsMarginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.OutputBytes = 950; // 5% saving, below the default 10% MarginalThresholdPercent

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Replaced.ShouldBe(1);
        result.Marginal.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ReplacePartial_CountsAsReplacedAndSaved()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.ReplaceOutcome = ReplaceOutcome.ReplacePartial;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Replaced.ShouldBe(1);
        result.BytesSaved.ShouldBe(500);
    }

    [Fact]
    public async Task RunAsync_ReplaceFailure_CleansTempAndCountsFailed()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.ReplaceOutcome = ReplaceOutcome.Failed;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

        result.Failed.ShouldBe(1);
        result.Replaced.ShouldBe(0);
        fakes.Calls.ShouldBe(
            ["probe", "classify", "space", "encode", "verify", "replace", "delete-temp"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsAndLeavesOriginalUntouched()
    {
        var fakes = PipelineFakes.Candidate();
        using var cts = new CancellationTokenSource();
        fakes.OnEncode = () => cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions(), cancellationToken: cts.Token));

        fakes.OriginalRemoved.ShouldBeFalse();
        fakes.Calls.ShouldContain("delete-temp"); // encode temp cleaned up
    }

    [Fact]
    public async Task RunAsync_EncoderThrowsDiskFull_RecordsFailureAndContinues()
    {
        // Two files: encoder throws IOException on the first, succeeds on the second.
        var fakes = PipelineFakes.TwoFiles();
        fakes.ThrowOnFirstEncode = new IOException("No space left on device");

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath, PipelineFakes.SecondFilePath], new TriageOptions());

        // First file recorded as EncodeFailed, second file was processed and replaced.
        result.Failed.ShouldBe(1);
        result.Replaced.ShouldBe(1);
        var failedFile = result.Files.Single(f => f.Outcome == TriageOutcome.EncodeFailed);
        failedFile.FilePath.ShouldBe(PipelineFakes.FilePath);
        var replacedFile = result.Files.Single(f => f.Outcome == TriageOutcome.Replaced);
        replacedFile.FilePath.ShouldBe(PipelineFakes.SecondFilePath);
    }

    [Fact]
    public async Task RunAsync_CoordinatorReturnsReplaceFailed_RecordsOutcomeAndContinues()
    {
        // Two files: replacer returns Failed for the first, succeeds for the second.
        var fakes = PipelineFakes.TwoFiles();
        fakes.FailFirstReplace = true;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath, PipelineFakes.SecondFilePath], new TriageOptions());

        // First file recorded as ReplaceFailed, second file was processed and replaced.
        result.Failed.ShouldBe(1);
        result.Replaced.ShouldBe(1);
        var failedFile = result.Files.Single(f => f.Outcome == TriageOutcome.ReplaceFailed);
        failedFile.FilePath.ShouldBe(PipelineFakes.FilePath);
        var replacedFile = result.Files.Single(f => f.Outcome == TriageOutcome.Replaced);
        replacedFile.FilePath.ShouldBe(PipelineFakes.SecondFilePath);
    }

    [Fact]
    public async Task RunAsync_EmptyFileList_ReturnsSummaryWithZeroCounts()
    {
        var fakes = PipelineFakes.Candidate();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [], new TriageOptions());

        result.Scanned.ShouldBe(0);
        result.Candidates.ShouldBe(0);
        result.Replaced.ShouldBe(0);
        result.Failed.ShouldBe(0);
        result.Skipped.ShouldBe(0);
        result.BytesSaved.ShouldBe(0);
        fakes.Calls.ShouldBeEmpty();
    }

    private sealed class PipelineFakes
    {
        internal const string FilePath = @"C:\Videos\clip.mov";
        internal const string SecondFilePath = @"C:\Videos\clip2.mov";

        public List<string> Calls { get; } = [];
        public HashSet<string> CreatedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public long AvailableBytes { get; set; } = long.MaxValue;
        public long SourceBytes { get; set; } = 1000;
        public long OutputBytes { get; set; } = 500;
        public bool ProbeSucceeds { get; set; } = true;
        public ClassificationOutcome Classification { get; set; } = ClassificationOutcome.Candidate;
        public EncodeOutcome EncodeOutcome { get; set; } = EncodeOutcome.Succeeded;
        public VerificationResult Verification { get; set; } =
            new() { Outcome = VerificationOutcome.Valid, Reason = "valid" };
        public ReplaceOutcome ReplaceOutcome { get; set; } = ReplaceOutcome.Replaced;
        public Action? OnEncode { get; set; }
        public bool OriginalRemoved { get; private set; }

        /// <summary>If set, the encoder throws this exception on the first encode call.</summary>
        public IOException? ThrowOnFirstEncode { get; set; }
        /// <summary>If true, the replacer returns Failed for the first file only.</summary>
        public bool FailFirstReplace { get; set; }
        private int _encodeCalls;
        private int _replaceCalls;

        public TriagePipeline Pipeline { get; }

        private PipelineFakes()
        {
            Pipeline = new TriagePipeline(
                new FakeRunLeaseFactory(),
                new FakeProbe(this),
                new FakeClassifier(this),
                new FakeEncoder(this),
                new FakeVerifier(this),
                new FakeReplacer(this),
                new FakeFileSystem(this),
                _ => new NoOpCompletedStore(),
                _ => new NoOpDeleteManifest(),
                _ => new NoOpResultLog());
        }

        public static PipelineFakes Candidate() => new();
        public static PipelineFakes LowBpp() => new() { Classification = ClassificationOutcome.SkipLowBpp };
        public static PipelineFakes TwoFiles() => new();

        internal void MarkOriginalRemoved() => OriginalRemoved = true;
        internal int IncrementEncodeCalls() => ++_encodeCalls;
        internal int IncrementReplaceCalls() => ++_replaceCalls;

        internal VideoStats Stats() => new()
        {
            FilePath = FilePath,
            CodecName = "h264",
            Width = 1920,
            Height = 1080,
            FramesPerSecond = 30,
            Duration = TimeSpan.FromMinutes(1),
            FileSizeBytes = SourceBytes,
            HasAudio = true
        };

        private sealed class FakeProbe(PipelineFakes f) : IFfprobeService
        {
            public Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
            {
                f.Calls.Add("probe");
                return Task.FromResult(f.ProbeSucceeds
                    ? new ProbeResult { FilePath = filePath, Stats = f.Stats() }
                    : new ProbeResult { FilePath = filePath, Failure = new ProbeFailure { FilePath = filePath, Message = "probe failed" } });
            }
        }

        private sealed class FakeClassifier(PipelineFakes f) : IVideoClassifier
        {
            public ClassificationResult Classify(VideoStats stats, TriageOptions? options = null)
            {
                f.Calls.Add("classify");
                return new ClassificationResult { Outcome = f.Classification, Reason = "classified", Stats = stats };
            }
        }

        private sealed class FakeEncoder(PipelineFakes f) : IVideoEncoder
        {
            public Task<EncodeResult> EncodeAsync(string inputPath, string outputPath,
                IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            {
                f.Calls.Add("encode");
                var callNumber = f.IncrementEncodeCalls();
                // Register the output file before invoking callbacks/cancellation checks so
                // that the catch/finally cleanup can correctly detect it via FileExists.
                f.CreatedFiles.Add(outputPath);
                f.OnEncode?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                // Throw IOException on first encode if requested (simulates disk-full mid-encode).
                if (callNumber == 1 && f.ThrowOnFirstEncode is not null)
                    throw f.ThrowOnFirstEncode;
                if (f.EncodeOutcome != EncodeOutcome.Succeeded)
                    f.CreatedFiles.Remove(outputPath);
                return Task.FromResult(new EncodeResult
                {
                    Outcome = f.EncodeOutcome,
                    OutputPath = outputPath,
                    Reason = "encoded"
                });
            }
        }

        private sealed class FakeVerifier(PipelineFakes f) : IOutputVerifier
        {
            public Task<VerificationResult> VerifyAsync(VideoStats source, string outputPath,
                TriageOptions options, CancellationToken cancellationToken = default)
            {
                f.Calls.Add("verify");
                return Task.FromResult(f.Verification);
            }
        }

        private sealed class FakeReplacer(PipelineFakes f) : ISafeReplacer
        {
            public ReplaceResult Replace(string originalPath, string verifiedReplacementPath, DeleteMode deleteMode)
            {
                f.Calls.Add("replace");
                var callNumber = f.IncrementReplaceCalls();
                // If FailFirstReplace is set, return Failed outcome for the first call only.
                var outcome = (f.FailFirstReplace && callNumber == 1) ? ReplaceOutcome.Failed : f.ReplaceOutcome;
                if (outcome is not ReplaceOutcome.Failed)
                {
                    f.MarkOriginalRemoved();
                    // Model that SafeReplacer consumed (moved) the replacement file.
                    f.CreatedFiles.Remove(verifiedReplacementPath);
                }
                return new ReplaceResult
                {
                    Outcome = outcome,
                    FinalPath = outcome == ReplaceOutcome.ReplacePartial
                        ? TempFileNaming.PartialPath(originalPath, Guid.Empty)
                        : Path.ChangeExtension(originalPath, ".mp4"),
                    Reason = outcome == ReplaceOutcome.Failed ? "replace failed" : "replaced",
                    OriginalRemoved = outcome is not ReplaceOutcome.Failed
                };
            }
        }

        private sealed class FakeFileSystem(PipelineFakes f) : IFileSystem
        {
            private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);
            public bool FileExists(string path) =>
                f.CreatedFiles.Contains(path) && !_deleted.Contains(path);
            public long GetFileLength(string path) => f.OutputBytes;
            public void CreateDirectory(string path) { }
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) { }
            public void MoveFile(string sourcePath, string destinationPath) { }
            public void DeleteFile(string path)
            {
                _deleted.Add(path);
                f.Calls.Add("delete-temp");
            }
            public long GetAvailableFreeSpace(string path)
            {
                f.Calls.Add("space");
                return f.AvailableBytes;
            }
            public DateTimeOffset GetLastWriteTimeUtc(string path) => DateTimeOffset.UnixEpoch;
        }

        private sealed class NoOpCompletedStore : ICompletedFileStore
        {
            public IReadOnlyList<CompletedFileEntry> Load() => [];
            public void Append(CompletedFileEntry entry) { }
        }

        private sealed class NoOpDeleteManifest : IDeleteManifest
        {
            public void Append(DeleteManifestEntry entry) { }
        }

        private sealed class NoOpResultLog : IResultLog
        {
            public void Append(ResultLogEntry entry) { }
        }

        private sealed class FakeRunLeaseFactory : IRunLeaseFactory
        {
            public IDisposable Acquire(string dataDirectory) => new FakeLease();
            private sealed class FakeLease : IDisposable { public void Dispose() { } }
        }
    }
}
