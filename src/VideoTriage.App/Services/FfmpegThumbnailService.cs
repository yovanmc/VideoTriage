using System.IO;
using System.Windows.Media.Imaging;
using VideoTriage.Core.Tools;

namespace VideoTriage.App.Services;

public sealed class FfmpegThumbnailService : IThumbnailService
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
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var pngPath = _tempFileFactory();
            try
            {
                await _runner.RunAsync(new ProcessRequest
                {
                    FileName = _ffmpegPath,
                    Arguments = ["-i", filePath, "-map", $"0:{streamIndex}", "-frames:v", "1", "-loglevel", "quiet", pngPath, "-y"],
                    Timeout = TimeSpan.FromSeconds(30)
                }, linked.Token);

                if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
                    return null;

                using var memStream = new MemoryStream(await File.ReadAllBytesAsync(pngPath, CancellationToken.None));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memStream;
                bitmap.DecodePixelWidth = 96;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
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
}
