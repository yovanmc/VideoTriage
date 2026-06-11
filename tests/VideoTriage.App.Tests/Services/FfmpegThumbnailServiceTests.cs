using System.IO;
using System.Threading;
using VideoTriage.App.Services;
using VideoTriage.Core.Tools;
using Shouldly;

namespace VideoTriage.App.Tests.Services;

public sealed class FfmpegThumbnailServiceTests
{
    private static ProcessResult Ok() => new()
    {
        ExitCode = 0,
        StandardOutput = "",
        Elapsed = TimeSpan.Zero
    };

    [Fact]
    public async Task GetAsync_UsesResolvedFfmpegPathAndDeletesTemporaryPng()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"vt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string? capturedFileName = null;
            var runner = new FakeProcessRunner
            {
                OnRun = request =>
                {
                    capturedFileName = request.FileName;
                    // Write TinyPng bytes to the output path (second-to-last argument, before "-y")
                    var outputPath = request.Arguments[^2];
                    File.WriteAllBytes(outputPath, TinyPng.Bytes);
                    return Ok();
                }
            };

            var service = new FfmpegThumbnailService(
                @"C:\tools\ffmpeg.exe",
                runner,
                tempFileFactory: () => Path.Combine(tempDir, $"vt_thumb_{Guid.NewGuid():N}.png"));

            // Act
            var bitmap = await OnStaAsync(() =>
                service.GetAsync(@"C:\videos\test.mp4", 0, CancellationToken.None));

            // Assert
            capturedFileName.ShouldBe(@"C:\tools\ffmpeg.exe");
            bitmap.ShouldNotBeNull();
            bitmap!.IsFrozen.ShouldBeTrue();
            Directory.GetFiles(tempDir).ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_MoreThanFourRequests_NeverRunsMoreThanFourProcesses()
    {
        // Arrange
        var blockRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessRunner
        {
            BlockUntil = blockRelease.Task,
            OnRun = _ => Ok()
        };

        var tempDir = Path.Combine(Path.GetTempPath(), $"vt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = new FfmpegThumbnailService(
                @"C:\tools\ffmpeg.exe",
                runner,
                tempFileFactory: () => Path.Combine(tempDir, $"vt_thumb_{Guid.NewGuid():N}.png"));

            // Act — launch 12 concurrent requests
            var cts = new CancellationTokenSource();
            var tasks = Enumerable.Range(0, 12)
                .Select(_ => Task.Run(() => service.GetAsync(@"C:\videos\test.mp4", 0, cts.Token)))
                .ToArray();

            // Wait until 4 are running
            await runner.WaitForStartsAsync(4);

            // Assert concurrency never exceeded 4
            runner.MaximumConcurrent.ShouldBe(4);

            // Release all blocked runners and await completion
            blockRelease.SetResult();
            await Task.WhenAll(tasks);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_Cancelled_KillsProcessAndDeletesTemporaryPng()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"vt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var runner = new FakeProcessRunner
            {
                BlockUntilCancellation = true,
                OnRun = _ => Ok()
            };

            var service = new FfmpegThumbnailService(
                @"C:\tools\ffmpeg.exe",
                runner,
                tempFileFactory: () => Path.Combine(tempDir, $"vt_thumb_{Guid.NewGuid():N}.png"));

            var cts = new CancellationTokenSource();

            // Act
            var task = Task.Run(() => service.GetAsync(@"C:\videos\test.mp4", 0, cts.Token));

            // Wait for runner to start then cancel
            await runner.Started;
            cts.Cancel();

            // Assert
            await Should.ThrowAsync<OperationCanceledException>(() => task);
            Directory.GetFiles(tempDir).ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_VideoStream_DoesNotMapStream()
    {
        // Arrange: a fake runner that records args and returns success (no output file → null result)
        ProcessRequest? captured = null;
        var runner = new FakeProcessRunner
        {
            OnRun = req =>
            {
                captured = req;
                return Ok();
            }
        };
        var svc = new FfmpegThumbnailService("ffmpeg", runner, () => Path.Combine(Path.GetTempPath(), $"vt_thumb_{Guid.NewGuid():N}.png"));

        // Act (result will be null because no file is written — that's fine)
        _ = await svc.GetAsync(@"C:\video.mp4", streamIndex: IThumbnailService.VideoStream, CancellationToken.None);

        // Assert
        captured.ShouldNotBeNull();
        captured!.Arguments.ShouldNotContain("-map");
    }

    // STA thread helper for BitmapImage which requires STA
    private static async Task<T> OnStaAsync<T>(Func<Task<T>> fn)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.SetResult(fn().GetAwaiter().GetResult()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return await tcs.Task;
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private int _current;
        private int _maximumConcurrent;
        private int _startCount;
        private int _targetStarts;
        private TaskCompletionSource? _startsTcs;

        public Func<ProcessRequest, ProcessResult>? OnRun { get; set; }
        public Task? BlockUntil { get; set; }
        public bool BlockUntilCancellation { get; set; }
        public int MaximumConcurrent => _maximumConcurrent;
        public Task Started => _startedTcs.Task;

        private readonly TaskCompletionSource _startedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForStartsAsync(int n)
        {
            lock (this)
            {
                if (_startCount >= n)
                    return Task.CompletedTask;
                _targetStarts = n;
                _startsTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _startsTcs.Task;
            }
        }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            int current;
            lock (this)
            {
                current = Interlocked.Increment(ref _current);
                if (current > _maximumConcurrent)
                    _maximumConcurrent = current;
                _startCount++;
                _startedTcs.TrySetResult();
                if (_startsTcs is not null && _startCount >= _targetStarts)
                    _startsTcs.TrySetResult();
            }

            try
            {
                if (BlockUntilCancellation)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else if (BlockUntil is not null)
                {
                    await BlockUntil;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return OnRun?.Invoke(request) ?? new ProcessResult { ExitCode = 0, StandardOutput = "", Elapsed = TimeSpan.Zero };
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }
}

internal static class TinyPng
{
    // Minimal valid 1x1 white PNG
    public static readonly byte[] Bytes = [
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A, // PNG signature
        0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52, // IHDR length+type
        0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01, // 1x1
        0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53, // 8-bit RGB
        0xDE,0x00,0x00,0x00,0x0C,0x49,0x44,0x41, // CRC + IDAT length+type
        0x54,0x08,0xD7,0x63,0xF8,0xCF,0xC0,0x00, // IDAT data
        0x00,0x00,0x02,0x00,0x01,0xE2,0x21,0xBC, // IDAT CRC
        0x33,0x00,0x00,0x00,0x00,0x49,0x45,0x4E, // IEND length+type
        0x44,0xAE,0x42,0x60,0x82              // IEND CRC
    ];
}
