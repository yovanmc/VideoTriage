using System.Windows.Media.Imaging;

namespace VideoTriage.App.Services;

public interface IThumbnailService
{
    Task<BitmapSource?> GetAsync(
        string filePath,
        int streamIndex,
        CancellationToken cancellationToken);
}
