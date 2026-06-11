using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using VideoTriage.Core.Tools;

namespace VideoTriage.App.Services;

public sealed class FfmpegThumbnailService : IThumbnailService, IDisposable
{
    private readonly string _ffmpegPath;
    private readonly IProcessRunner _runner;
    private readonly Func<string> _tempFileFactory;
    private readonly SemaphoreSlim _semaphore = new(4, 4);

    public FfmpegThumbnailService(
        string ffmpegPath,
        IProcessRunner runner,
        Func<string>? tempFileFactory = null)
    {
        _ffmpegPath = ffmpegPath;
        _runner = runner;
        _tempFileFactory = tempFileFactory
            ?? (() => Path.Combine(Path.GetTempPath(), $"vt_thumb_{Guid.NewGuid():N}.png"));
    }

    public async Task<BitmapSource?> GetAsync(string filePath, int streamIndex, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var pngPath = _tempFileFactory();
            try
            {
                var arguments = streamIndex == IThumbnailService.VideoStream
                    ? new[] { "-i", filePath, "-frames:v", "1", "-loglevel", "quiet", pngPath, "-y" }
                    : new[] { "-i", filePath, "-map", $"0:{streamIndex}", "-frames:v", "1", "-loglevel", "quiet", pngPath, "-y" };

                await _runner.RunAsync(new ProcessRequest
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    Timeout = TimeSpan.FromSeconds(30)
                }, cancellationToken);

                if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
                    return null;

                return LoadFrozenBitmap(pngPath);
            }
            finally
            {
                try { File.Delete(pngPath); } catch { }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();

    // A BitmapImage decoded with OnLoad and then Frozen has no thread affinity, so it can be
    // built directly on the calling worker thread — no dedicated STA thread (and its create/Join
    // cost) is needed. This runs once per file, so avoiding a thread spin-up per thumbnail matters
    // on large folders.
    private static BitmapSource LoadFrozenBitmap(string pngPath)
    {
        using var stream = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = stream;
        img.DecodePixelWidth = 96;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
