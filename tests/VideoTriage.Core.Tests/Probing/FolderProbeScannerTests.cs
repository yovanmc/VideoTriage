using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class FolderProbeScannerTests
{
    [Fact]
    public async Task ScanAsync_ClassifiesSuccessfulProbes()
    {
        using var temp = new TempDirectory();
        var file = temp.File("candidate.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [file] = Success(file, bpp: 0.20)
            }
        };

        var results = await CreateScanner(service).ScanAsync(temp.Path);

        results.Single().Classification.ShouldNotBeNull();
        results.Single().Classification!.Outcome.ShouldBe(ClassificationOutcome.Candidate);
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

        var results = await CreateScanner(service).ScanAsync(temp.Path);

        results.Count.ShouldBe(2);
        results[0].Failure.ShouldNotBeNull();
        results[1].Classification!.Outcome.ShouldBe(ClassificationOutcome.Candidate);
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
    public async Task ScanAsync_PreservesSortedDiscoveryOrder()
    {
        using var temp = new TempDirectory();
        var second = temp.File("z.mp4");
        var first = temp.File("a.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [first] = Success(first, bpp: 0.20),
                [second] = Success(second, bpp: 0.20)
            }
        };

        var results = await CreateScanner(service).ScanAsync(temp.Path);

        results.Select(result => Path.GetFileName(result.FilePath)).ShouldBe(new[] { "a.mp4", "z.mp4" });
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellationBeforeRemainingFiles()
    {
        using var temp = new TempDirectory();
        var first = temp.File("a.mp4");
        temp.File("b.mp4");
        using var cts = new CancellationTokenSource();
        var service = new FakeFfprobeService
        {
            Results =
            {
                [first] = Success(first, bpp: 0.20)
            },
            CancelAfterFirstProbe = cts
        };

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateScanner(service).ScanAsync(temp.Path, cancellationToken: cts.Token));
    }

    private static FolderProbeScanner CreateScanner(IFfprobeService service) =>
        new(new VideoFileDiscovery(), service, new BppClassifier());

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
        private int _probeCount;
        public Dictionary<string, ProbeResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CancellationTokenSource? CancelAfterFirstProbe { get; init; }

        public Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _probeCount++;
            if (_probeCount == 1)
            {
                CancelAfterFirstProbe?.Cancel();
            }

            return Task.FromResult(Results[filePath]);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
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
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
