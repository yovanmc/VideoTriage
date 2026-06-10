using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class FolderProbeScannerTests
{
    [Fact]
    public async Task Scanner_IsConsumableThroughInterfaceWithAbstractDependencies()
    {
        IVideoFileDiscovery discovery = new StubVideoFileDiscovery();
        IVideoClassifier classifier = new StubVideoClassifier();
        IFolderProbeScanner scanner = new FolderProbeScanner(
            discovery,
            new FakeFfprobeService(),
            classifier);

        var summary = await scanner.ScanAsync(@"C:\videos");

        summary.FilesDiscovered.ShouldBe(0);
        summary.CandidateCount.ShouldBe(0);
        summary.ProbeFailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task ScanAsync_ClassifiesSuccessfulProbes()
    {
        using var temp = new TempDirectory();
        var file = temp.File("candidate.mp4");
        var service = new FakeFfprobeService
        {
            Results = { [file] = Success(file, bpp: 0.20) }
        };
        var received = new List<ProbeResult>();

        var summary = await CreateScanner(service).ScanAsync(
            temp.Path,
            progress: new InlineProgress<ProbeResult>(received.Add));

        received.ShouldHaveSingleItem().Classification!.Outcome.ShouldBe(ClassificationOutcome.Candidate);
        summary.CandidateCount.ShouldBe(1);
    }

    [Fact]
    public async Task ScanAsync_PreservesProbeFailureAndContinues()
    {
        using var temp = new TempDirectory();
        var bad = temp.File("bad.mp4");
        var good = temp.File("good.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [bad] = Failure(bad, "bad metadata"),
                [good] = Success(good, bpp: 0.20)
            }
        };
        var received = new List<ProbeResult>();

        var summary = await CreateScanner(service).ScanAsync(
            temp.Path,
            progress: new InlineProgress<ProbeResult>(received.Add));

        received.Count.ShouldBe(2);
        received.ShouldContain(r => r.Failure != null);
        received.ShouldContain(r => r.Classification != null && r.Classification.Outcome == ClassificationOutcome.Candidate);
        summary.FilesDiscovered.ShouldBe(2);
        summary.CandidateCount.ShouldBe(1);
        summary.ProbeFailureCount.ShouldBe(1);
    }

    [Fact]
    public async Task ScanAsync_ReportsProgressOncePerCompletedFile()
    {
        using var temp = new TempDirectory();
        var first = temp.File("a.mp4");
        var second = temp.File("b.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [first] = Success(first, bpp: 0.20),
                [second] = Success(second, bpp: 0.10)
            }
        };
        var progressResults = new List<ProbeResult>();

        await CreateScanner(service).ScanAsync(
            temp.Path,
            progress: new InlineProgress<ProbeResult>(progressResults.Add));

        progressResults.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ScanAsync_ReturnsCorrectFilesDiscoveredCount()
    {
        using var temp = new TempDirectory();
        var a = temp.File("a.mp4");
        var b = temp.File("b.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [a] = Success(a, bpp: 0.20),
                [b] = Success(b, bpp: 0.20)
            }
        };

        var summary = await CreateScanner(service).ScanAsync(temp.Path);

        summary.FilesDiscovered.ShouldBe(2);
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellation()
    {
        using var temp = new TempDirectory();
        temp.File("a.mp4");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateScanner(new FakeFfprobeService()).ScanAsync(temp.Path, cancellationToken: cts.Token));
    }

    private static FolderProbeScanner CreateScanner(IFfprobeService service, int maxParallelism = 2) =>
        new(new VideoFileDiscovery(), service, new BppClassifier(), maxParallelism);

    private static ProbeResult Success(string filePath, double bpp) =>
        new()
        {
            FilePath = filePath,
            Stats = new VideoStats
            {
                FilePath = filePath,
                CodecName = "h264",
                Width = 1920,
                Height = 1080,
                FramesPerSecond = 30,
                Duration = TimeSpan.FromSeconds(60),
                FileSizeBytes = 30_000_000,
                VideoBitrateBitsPerSecond = (long)Math.Round(bpp * 1920 * 1080 * 30),
                HasAudio = true
            }
        };

    private static ProbeResult Failure(string filePath, string message) =>
        new()
        {
            FilePath = filePath,
            Failure = new ProbeFailure
            {
                FilePath = filePath,
                Message = message
            }
        };

    private sealed class FakeFfprobeService : IFfprobeService
    {
        public Dictionary<string, ProbeResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Results[filePath]);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StubVideoFileDiscovery : IVideoFileDiscovery
    {
        public IEnumerable<string> EnumerateVideos(
            string folderPath,
            TriageOptions? options = null,
            bool recursive = false,
            IProgress<DiscoveryWarning>? warnings = null,
            CancellationToken cancellationToken = default) => [];
    }

    private sealed class StubVideoClassifier : IVideoClassifier
    {
        public ClassificationResult Classify(
            VideoStats stats,
            TriageOptions? options = null) =>
            throw new NotSupportedException();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            System.IO.File.WriteAllText(fullPath, string.Empty);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
