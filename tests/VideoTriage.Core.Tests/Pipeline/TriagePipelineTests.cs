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

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Skipped.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_ProbeFailure_CountsInvalid()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.ProbeSucceeds = false;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Invalid.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe"]);
    }

    [Fact]
    public async Task RunAsync_DryRun_StopsAfterClassification()
    {
        var fakes = PipelineFakes.Candidate();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { DryRun = true });

        result.Skipped.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_InsufficientSpace_KeepsOriginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.AvailableBytes = 10;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Failed.ShouldBe(1);
        fakes.Calls.ShouldNotContain("replace");
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_EncodeFailure_KeepsOriginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.EncodeOutcome = EncodeOutcome.Failed;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Failed.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify", "space", "encode"]);
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

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Invalid.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify", "space", "encode", "verify", "delete-temp"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_OutputNotSmaller_KeepsOriginalAndCountsGrew()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.OutputBytes = 1000; // equal to source => not smaller

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Grew.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify", "space", "encode", "verify", "delete-temp"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_SmallerVerifiedOutput_CallsSafeReplacer()
    {
        var fakes = PipelineFakes.Candidate();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Replaced.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify", "space", "encode", "verify", "replace"]);
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

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Replaced.ShouldBe(1);
        result.Marginal.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ReplacePartial_CountsAsReplacedAndSaved()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.ReplaceOutcome = ReplaceOutcome.ReplacePartial;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Replaced.ShouldBe(1);
        result.BytesSaved.ShouldBe(500);
    }

    [Fact]
    public async Task RunAsync_ReplaceFailure_CleansTempAndCountsFailed()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.ReplaceOutcome = ReplaceOutcome.Failed;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Failed.ShouldBe(1);
        result.Replaced.ShouldBe(0);
        fakes.Calls.ShouldBe(
            ["discover", "probe", "classify", "space", "encode", "verify", "replace", "delete-temp"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsAndLeavesOriginalUntouched()
    {
        var fakes = PipelineFakes.Candidate();
        using var cts = new CancellationTokenSource();
        fakes.OnEncode = () => cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions(), cancellationToken: cts.Token));

        fakes.OriginalRemoved.ShouldBeFalse();
        fakes.Calls.ShouldContain("delete-temp"); // encode temp cleaned up
    }

    private sealed class PipelineFakes
    {
        internal const string FilePath = @"C:\Videos\clip.mov";

        public List<string> Calls { get; } = [];
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

        public TriagePipeline Pipeline { get; }

        private PipelineFakes()
        {
            Pipeline = new TriagePipeline(
                new FakeDiscovery(this),
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

        internal void MarkOriginalRemoved() => OriginalRemoved = true;

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

        private sealed class FakeDiscovery(PipelineFakes f) : IVideoFileDiscovery
        {
            public IReadOnlyList<string> FindVideos(string folderPath, TriageOptions? options = null, bool recursive = false)
            {
                f.Calls.Add("discover");
                return [FilePath];
            }
        }

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
                f.OnEncode?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
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
                if (f.ReplaceOutcome is not ReplaceOutcome.Failed)
                    f.MarkOriginalRemoved();
                return new ReplaceResult
                {
                    Outcome = f.ReplaceOutcome,
                    FinalPath = f.ReplaceOutcome == ReplaceOutcome.ReplacePartial
                        ? TempFileNaming.PartialPath(originalPath, 1)
                        : Path.ChangeExtension(originalPath, ".mp4"),
                    Reason = "replaced",
                    OriginalRemoved = f.ReplaceOutcome is not ReplaceOutcome.Failed
                };
            }
        }

        private sealed class FakeFileSystem(PipelineFakes f) : IFileSystem
        {
            public bool FileExists(string path) => true;
            public long GetFileLength(string path) => f.OutputBytes;
            public void CreateDirectory(string path) { }
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) { }
            public void MoveFile(string sourcePath, string destinationPath) { }
            public void DeleteFile(string path) => f.Calls.Add("delete-temp");
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
    }
}
