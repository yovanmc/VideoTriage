using Shouldly;
using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Poster;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Pipeline;

public sealed class TriagePipelinePosterTests
{
    [Fact]
    public async Task RunAsync_PosterEnabled_CallsEmbedderBetweenVerifyAndReplace()
    {
        var fakes = PipelinePosterFakes.Successful(embedderReturns: "with-poster.mp4");

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = true });

        fakes.Calls.ShouldBe(
        [
            "encode",
            "verify",
            "embed-poster",
            $"delete:{fakes.EncodePath}",
            "replace:with-poster.mp4"
        ]);
    }

    [Fact]
    public async Task RunAsync_PosterDisabled_DoesNotCallEmbedder()
    {
        var fakes = PipelinePosterFakes.Successful(embedderReturns: "with-poster.mp4");

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = false });

        fakes.Calls.ShouldBe(["encode", "verify", $"replace:{fakes.EncodePath}"]);
    }

    [Fact]
    public async Task RunAsync_PosterFailure_ReplacesOriginalVerifiedEncode()
    {
        var fakes = PipelinePosterFakes.Successful(embedderReturns: null);

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = true });

        fakes.Calls.ShouldContain($"replace:{fakes.EncodePath}");
    }

    [Fact]
    public async Task RunAsync_PosterPushesOutputOverOriginal_KeepsOriginal()
    {
        var fakes = PipelinePosterFakes.Successful(embedderReturns: "with-poster.mp4");
        fakes.SetFileLength("with-poster.mp4", fakes.SourceBytes + 1);

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = true });

        result.Grew.ShouldBe(1);
        fakes.OriginalRemoved.ShouldBeFalse();
        fakes.Calls.ShouldNotContain("replace:with-poster.mp4");
    }

    [Fact]
    public async Task RunAsync_PosterReplacementFails_CleansTempsAndKeepsOriginal()
    {
        var fakes = PipelinePosterFakes.Successful(embedderReturns: "with-poster.mp4");
        fakes.ReplaceOutcome = ReplaceOutcome.Failed;

        await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = true });

        fakes.OriginalRemoved.ShouldBeFalse();
        fakes.Calls.ShouldContain($"delete:{fakes.EncodePath}");
        fakes.Calls.ShouldContain("delete:with-poster.mp4");
    }

    private sealed class PipelinePosterFakes
    {
        private const string FilePath = @"C:\Videos\clip.mov";
        private readonly Dictionary<string, long> _lengths = new(StringComparer.OrdinalIgnoreCase);
        private readonly string? _embedderReturns;
        internal readonly HashSet<string> _created = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Calls { get; } = [];
        public long SourceBytes { get; } = 1000;
        public long OutputBytes { get; } = 500;
        public bool OriginalRemoved { get; private set; }
        public ReplaceOutcome ReplaceOutcome { get; set; } = ReplaceOutcome.Replaced;
        public string EncodePath { get; private set; } = "";
        public TriagePipeline Pipeline { get; }

        private PipelinePosterFakes(string? embedderReturns)
        {
            _embedderReturns = embedderReturns;
            Pipeline = new TriagePipeline(
                new FakeRunLeaseFactory(),
                new FakeDiscovery(),
                new FakeProbe(this),
                new FakeClassifier(),
                new FakeEncoder(this),
                new FakeVerifier(this),
                new FakeReplacer(this),
                new FakeFileSystem(this),
                _ => new NoOpCompletedStore(),
                _ => new NoOpDeleteManifest(),
                _ => new NoOpResultLog(),
                new FakePosterEmbedder(this));
        }

        public static PipelinePosterFakes Successful(string? embedderReturns) => new(embedderReturns);

        public void SetFileLength(string path, long length) => _lengths[path] = length;

        private VideoStats Stats() => new()
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

        private long Length(string path)
        {
            if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase))
                return SourceBytes;
            return _lengths.TryGetValue(path, out var length) ? length : OutputBytes;
        }

        private sealed class FakeDiscovery : IVideoFileDiscovery
        {
            public IReadOnlyList<string> FindVideos(
                string folderPath,
                TriageOptions? options = null,
                bool recursive = false) =>
                [FilePath];
        }

        private sealed class FakeProbe(PipelinePosterFakes f) : IFfprobeService
        {
            public Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
                Task.FromResult(new ProbeResult { FilePath = filePath, Stats = f.Stats() });
        }

        private sealed class FakeClassifier : IVideoClassifier
        {
            public ClassificationResult Classify(VideoStats stats, TriageOptions? options = null) =>
                new() { Outcome = ClassificationOutcome.Candidate, Reason = "candidate", Stats = stats };
        }

        private sealed class FakeEncoder(PipelinePosterFakes f) : IVideoEncoder
        {
            public Task<EncodeResult> EncodeAsync(
                string inputPath,
                string outputPath,
                IProgress<double>? progress = null,
                CancellationToken cancellationToken = default)
            {
                f.EncodePath = outputPath;
                f._created.Add(outputPath);
                f.Calls.Add("encode");
                return Task.FromResult(new EncodeResult
                {
                    Outcome = EncodeOutcome.Succeeded,
                    OutputPath = outputPath,
                    Reason = "encoded"
                });
            }
        }

        private sealed class FakeVerifier(PipelinePosterFakes f) : IOutputVerifier
        {
            public Task<VerificationResult> VerifyAsync(
                VideoStats source,
                string outputPath,
                TriageOptions options,
                CancellationToken cancellationToken = default)
            {
                f.Calls.Add("verify");
                return Task.FromResult(new VerificationResult
                {
                    Outcome = VerificationOutcome.Valid,
                    Reason = "valid"
                });
            }
        }

        private sealed class FakePosterEmbedder(PipelinePosterFakes f) : IPosterEmbedder
        {
            public Task<PosterEmbedResult> EmbedAsync(
                string verifiedEncodePath,
                VideoStats source,
                TriageOptions options,
                CancellationToken cancellationToken = default)
            {
                f.Calls.Add("embed-poster");
                var outputPath = f._embedderReturns ?? verifiedEncodePath;
                if (f._embedderReturns is not null)
                    f._created.Add(f._embedderReturns);
                return Task.FromResult(new PosterEmbedResult
                {
                    OutputPath = outputPath,
                    Embedded = f._embedderReturns is not null,
                    Reason = "poster"
                });
            }
        }

        private sealed class FakeReplacer(PipelinePosterFakes f) : ISafeReplacer
        {
            public ReplaceResult Replace(string originalPath, string verifiedReplacementPath, DeleteMode deleteMode)
            {
                f.Calls.Add($"replace:{verifiedReplacementPath}");
                f.OriginalRemoved = f.ReplaceOutcome == ReplaceOutcome.Replaced;
                if (f.OriginalRemoved)
                {
                    // Model that SafeReplacer consumed (moved) the replacement file.
                    f._created.Remove(verifiedReplacementPath);
                }
                return new ReplaceResult
                {
                    Outcome = f.ReplaceOutcome,
                    FinalPath = Path.ChangeExtension(originalPath, ".mp4"),
                    Reason = f.ReplaceOutcome == ReplaceOutcome.Replaced ? "replaced" : "failed",
                    OriginalRemoved = f.OriginalRemoved
                };
            }
        }

        private sealed class FakeFileSystem(PipelinePosterFakes f) : IFileSystem
        {
            private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);
            public bool FileExists(string path) =>
                f._created.Contains(path) && !_deleted.Contains(path);
            public long GetFileLength(string path) => f.Length(path);
            public void CreateDirectory(string path) { }
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) { }
            public void MoveFile(string sourcePath, string destinationPath) { }
            public void DeleteFile(string path)
            {
                _deleted.Add(path);
                f.Calls.Add($"delete:{path}");
            }
            public long GetAvailableFreeSpace(string path) => long.MaxValue;
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
