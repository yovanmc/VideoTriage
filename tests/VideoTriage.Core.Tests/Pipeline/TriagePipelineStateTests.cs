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

public sealed class TriagePipelineStateTests
{
    [Fact]
    public async Task RunAsync_MatchingCompletedEntry_DoesNotProbeOrEncode()
    {
        var fakes = PipelineStateFakes.WithCompletedEntry();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.ProbeCalls.ShouldBe(0);
        fakes.CompletedLoadCalls.ShouldBe(1);
        fakes.CompletedAppends.ShouldBeEmpty();
        fakes.ManifestAppends.ShouldBeEmpty();
        fakes.ResultAppends.ShouldBeEmpty();
        result.Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ChangedCompletedEntry_IsRetriaged()
    {
        var fakes = PipelineStateFakes.WithCompletedEntry();
        fakes.SourceBytes++;

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.ProbeCalls.ShouldBe(1);
        fakes.CompletedAppends.Single().SourceLength.ShouldBe(fakes.SourceBytes);
    }

    [Fact]
    public async Task RunAsync_ChangedLastWrite_IsRetriaged()
    {
        var fakes = PipelineStateFakes.WithCompletedEntry();
        fakes.SourceLastWrite = fakes.SourceLastWrite.AddMinutes(1);

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.ProbeCalls.ShouldBe(1);
        fakes.CompletedAppends.Single().SourceLastWriteUtc.ShouldBe(fakes.SourceLastWrite);
    }

    [Fact]
    public async Task RunAsync_DuplicateCompletedEntries_UsesLatestIdentity()
    {
        var fakes = PipelineStateFakes.WithCompletedEntry(sourceLength: 999);
        fakes.AddCompletedEntry(sourceLength: fakes.SourceBytes);

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.ProbeCalls.ShouldBe(0);
        fakes.CompletedLoadCalls.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_InvalidCompletedPath_DoesNotBlockRun()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        fakes.AddCompletedEntry(sourceLength: fakes.SourceBytes, sourcePath: "\0");

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.ProbeCalls.ShouldBe(1);
        fakes.CompletedAppends.Single().Outcome.ShouldBe(TriageOutcome.Replaced);
    }

    [Fact]
    public async Task RunAsync_Replaced_AppendsCompletedManifestAndResult()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.CompletedAppends.Single().Outcome.ShouldBe(TriageOutcome.Replaced);
        var manifest = fakes.ManifestAppends.Single();
        manifest.OriginalPath.ShouldBe(PipelineStateFakes.FilePath);
        manifest.OriginalBytes.ShouldBe(1000);
        manifest.ReplacementBytes.ShouldBe(500);
        manifest.SavedPercent.ShouldBe(50, 0.01);
        fakes.ResultAppends.Single().Outcome.ShouldBe(TriageOutcome.Replaced);
        fakes.CompletedLoadCalls.ShouldBe(1);
        fakes.CreatedDirectories.ShouldBe([@"C:\Videos\_videotriage_data"]);
        fakes.StoreFactoryPaths.ShouldBe(
        [
            @"C:\Videos\_videotriage_data",
            @"C:\Videos\_videotriage_data",
            @"C:\Videos\_videotriage_data"
        ]);
    }

    [Fact]
    public async Task RunAsync_SkippedOutcome_WritesCompletedButNoManifest()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        fakes.Classification = ClassificationOutcome.SkipLowBpp;

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.CompletedAppends.Single().Outcome.ShouldBe(TriageOutcome.SkippedLowBpp);
        fakes.ManifestAppends.ShouldBeEmpty();
        fakes.ResultAppends.Single().Outcome.ShouldBe(TriageOutcome.SkippedLowBpp);
    }

    [Fact]
    public async Task RunAsync_ProbeFailure_WritesResultButDoesNotMarkCompleted()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        fakes.ProbeSucceeds = false;

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.ResultAppends.Single().Outcome.ShouldBe(TriageOutcome.InvalidMetadata);
        fakes.CompletedAppends.ShouldBeEmpty();
        fakes.ManifestAppends.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_ReplacementFailure_RemainsRetryableAndDoesNotWriteManifest()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        fakes.ReplaceOutcome = ReplaceOutcome.Failed;
        fakes.OriginalRemoved = false;

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.CompletedAppends.ShouldBeEmpty();
        fakes.ManifestAppends.ShouldBeEmpty();
        fakes.ResultAppends.Single().Outcome.ShouldBe(TriageOutcome.EncodeFailed);
    }

    [Fact]
    public async Task RunAsync_DryRun_PerformsNoPersistentWrites()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { DryRun = true });

        fakes.CompletedAppends.ShouldBeEmpty();
        fakes.ManifestAppends.ShouldBeEmpty();
        fakes.ResultAppends.ShouldBeEmpty();
        fakes.CreatedDirectories.ShouldBeEmpty();
        fakes.StoreFactoryCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_NonDryRun_AcquiresRunLease()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.LeaseAcquireCalls.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotAcquireRunLease()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { DryRun = true });

        fakes.LeaseAcquireCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_RecoveryUnrecoverable_ThrowsBeforeProbe()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        fakes.RecoveryUnrecoverableCount = 1;

        var ex = await Should.ThrowAsync<ReplacementRecoveryRequiredException>(
            () => fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions()));

        ex.Entries.Count.ShouldBe(1);
        fakes.ProbeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_RecoveryOnlyRecovered_RunProceedsNormally()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        fakes.RecoveryUnrecoverableCount = 0; // no unrecoverable entries

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        fakes.ProbeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_DryRun_RecoveryNotInvoked()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        fakes.RecoveryUnrecoverableCount = 1; // would fail if invoked

        // DryRun must skip recovery entirely — no exception should be thrown
        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { DryRun = true });

        fakes.RecoveryCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_NonDryRun_SavesActiveRunBeforeEachFileAndClearsAfter()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());
        fakes.ActiveRunSaves.ShouldNotBeEmpty();
        fakes.ActiveRunSaves.Last().CurrentFile.ShouldBe(PipelineStateFakes.FilePath);
        fakes.ActiveRunClears.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotSaveActiveRun()
    {
        var fakes = PipelineStateFakes.WithSuccessfulReplacement();
        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { DryRun = true });
        fakes.ActiveRunSaves.ShouldBeEmpty();
    }

    private sealed class PipelineStateFakes
    {
        internal const string FilePath = @"C:\Videos\clip.mov";
        private static readonly DateTimeOffset InitialLastWrite =
            DateTimeOffset.Parse("2026-05-01T12:00:00Z");

        public long SourceBytes { get; set; } = 1000;
        public long OutputBytes { get; set; } = 500;
        public DateTimeOffset SourceLastWrite { get; set; } = InitialLastWrite;
        public ClassificationOutcome Classification { get; set; } = ClassificationOutcome.Candidate;
        public bool ProbeSucceeds { get; set; } = true;
        public ReplaceOutcome ReplaceOutcome { get; set; } = ReplaceOutcome.Replaced;
        public bool OriginalRemoved { get; set; } = true;
        public int ProbeCalls { get; private set; }
        public int CompletedLoadCalls { get; private set; }
        public int StoreFactoryCalls { get; private set; }
        public int LeaseAcquireCalls => _leaseFactory.AcquireCalls;
        public int RecoveryCalls { get; private set; }
        public int RecoveryUnrecoverableCount { get; set; } = 0;

        public List<CompletedFileEntry> CompletedAppends { get; } = [];
        public List<DeleteManifestEntry> ManifestAppends { get; } = [];
        public List<ResultLogEntry> ResultAppends { get; } = [];
        public List<string> CreatedDirectories { get; } = [];
        public List<string> StoreFactoryPaths { get; } = [];
        public List<ActiveRunState> ActiveRunSaves { get; } = [];
        public int ActiveRunClears { get; private set; }
        internal void RecordActiveRunClear() => ActiveRunClears++;

        private readonly List<CompletedFileEntry> _preloaded = [];
        private readonly FakeRunLeaseFactory _leaseFactory = new();
        public TriagePipeline Pipeline { get; }

        private PipelineStateFakes()
        {
            Pipeline = new TriagePipeline(
                _leaseFactory,
                new FakeDiscovery(),
                new FakeProbe(this),
                new FakeClassifier(this),
                new FakeEncoder(),
                new FakeVerifier(),
                new FakeReplacer(this),
                new FakeFileSystem(this),
                path =>
                {
                    StoreFactoryCalls++;
                    StoreFactoryPaths.Add(path);
                    return new FakeCompletedStore(this);
                },
                path =>
                {
                    StoreFactoryCalls++;
                    StoreFactoryPaths.Add(path);
                    return new FakeDeleteManifest(this);
                },
                path =>
                {
                    StoreFactoryCalls++;
                    StoreFactoryPaths.Add(path);
                    return new FakeResultLog(this);
                },
                recoveryFactory: path =>
                {
                    RecoveryCalls++;
                    return new FakeRecovery(this);
                },
                activeRunJournalFactory: _ => new FakeActiveRunJournal(this));
        }

        public static PipelineStateFakes WithSuccessfulReplacement() => new();

        public static PipelineStateFakes WithCompletedEntry(long? sourceLength = null)
        {
            var fakes = new PipelineStateFakes();
            fakes.AddCompletedEntry(sourceLength ?? fakes.SourceBytes);
            return fakes;
        }

        public void AddCompletedEntry(long sourceLength, string sourcePath = FilePath)
        {
            _preloaded.Add(new CompletedFileEntry
            {
                SourcePath = sourcePath,
                SourceLength = sourceLength,
                SourceLastWriteUtc = SourceLastWrite,
                Outcome = TriageOutcome.Replaced,
                CompletedAtUtc = SourceLastWrite
            });
        }

        internal void RecordProbe() => ProbeCalls++;
        internal void RecordCompletedLoad() => CompletedLoadCalls++;

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

        private sealed class FakeDiscovery : IVideoFileDiscovery
        {
            public IReadOnlyList<string> FindVideos(string folderPath, TriageOptions? options = null, bool recursive = false) =>
                [FilePath];
        }

        private sealed class FakeProbe(PipelineStateFakes f) : IFfprobeService
        {
            public Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
            {
                f.RecordProbe();
                return Task.FromResult(f.ProbeSucceeds
                    ? new ProbeResult { FilePath = filePath, Stats = f.Stats() }
                    : new ProbeResult
                    {
                        FilePath = filePath,
                        Failure = new ProbeFailure { FilePath = filePath, Message = "probe failed" }
                    });
            }
        }

        private sealed class FakeClassifier(PipelineStateFakes f) : IVideoClassifier
        {
            public ClassificationResult Classify(VideoStats stats, TriageOptions? options = null) =>
                new() { Outcome = f.Classification, Reason = "classified", Stats = stats };
        }

        private sealed class FakeEncoder : IVideoEncoder
        {
            public Task<EncodeResult> EncodeAsync(string inputPath, string outputPath,
                IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(new EncodeResult { Outcome = EncodeOutcome.Succeeded, OutputPath = outputPath, Reason = "ok" });
        }

        private sealed class FakeVerifier : IOutputVerifier
        {
            public Task<VerificationResult> VerifyAsync(VideoStats source, string outputPath,
                TriageOptions options, CancellationToken cancellationToken = default) =>
                Task.FromResult(new VerificationResult { Outcome = VerificationOutcome.Valid, Reason = "valid" });
        }

        private sealed class FakeReplacer(PipelineStateFakes f) : ISafeReplacer
        {
            public ReplaceResult Replace(string originalPath, string verifiedReplacementPath, DeleteMode deleteMode) =>
                new()
                {
                    Outcome = f.ReplaceOutcome,
                    FinalPath = Path.ChangeExtension(originalPath, ".mp4"),
                    Reason = "replaced",
                    OriginalRemoved = f.OriginalRemoved
                };
        }

        private sealed class FakeFileSystem(PipelineStateFakes f) : IFileSystem
        {
            public bool FileExists(string path) => true;
            public long GetFileLength(string path) => TempFileNaming.IsTempArtifact(path) ? f.OutputBytes : f.SourceBytes;
            public void CreateDirectory(string path) => f.CreatedDirectories.Add(path);
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) { }
            public void MoveFile(string sourcePath, string destinationPath) { }
            public void DeleteFile(string path) { }
            public long GetAvailableFreeSpace(string path) => long.MaxValue;
            public DateTimeOffset GetLastWriteTimeUtc(string path) => f.SourceLastWrite;
        }

        private sealed class FakeCompletedStore(PipelineStateFakes f) : ICompletedFileStore
        {
            public IReadOnlyList<CompletedFileEntry> Load()
            {
                f.RecordCompletedLoad();
                return f._preloaded;
            }
            public void Append(CompletedFileEntry entry) => f.CompletedAppends.Add(entry);
        }

        private sealed class FakeDeleteManifest(PipelineStateFakes f) : IDeleteManifest
        {
            public void Append(DeleteManifestEntry entry) => f.ManifestAppends.Add(entry);
        }

        private sealed class FakeResultLog(PipelineStateFakes f) : IResultLog
        {
            public void Append(ResultLogEntry entry) => f.ResultAppends.Add(entry);
        }

        internal sealed class FakeRunLeaseFactory : IRunLeaseFactory
        {
            public int AcquireCalls { get; private set; }
            public IDisposable Acquire(string dataDirectory)
            {
                AcquireCalls++;
                return new FakeLease();
            }
            private sealed class FakeLease : IDisposable { public void Dispose() { } }
        }

        private sealed class FakeRecovery(PipelineStateFakes f) : VideoTriage.Core.State.IReplacementRecovery
        {
            public VideoTriage.Core.State.ReplacementRecoveryReport Recover()
            {
                var unrecoverable = Enumerable.Range(0, f.RecoveryUnrecoverableCount)
                    .Select(i => new VideoTriage.Core.State.UnrecoverableEntry
                    {
                        OriginalPath = $@"C:\videos\broken{i}.mov",
                        Detail = "Both files missing."
                    })
                    .ToList();
                return new VideoTriage.Core.State.ReplacementRecoveryReport
                {
                    Recovered = [],
                    Cleaned = [],
                    Unrecoverable = unrecoverable
                };
            }
        }

        private sealed class FakeActiveRunJournal(PipelineStateFakes f) : VideoTriage.Core.State.IActiveRunJournal
        {
            public void Save(VideoTriage.Core.State.ActiveRunState state) => f.ActiveRunSaves.Add(state);
            public void Clear() => f.RecordActiveRunClear();
            public VideoTriage.Core.State.ActiveRunState? Load() => null;
        }
    }
}
