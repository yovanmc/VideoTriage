using System.Windows.Media.Imaging;

namespace VideoTriage.App.Services;

public interface IThumbnailService
{
    /// <summary>
    /// Pass as <paramref name="streamIndex"/> to extract a frame from the first video stream
    /// rather than a specific attached-picture stream.
    /// </summary>
    const int VideoStream = -1;

    /// <param name="streamIndex">
    /// The ffprobe stream index of an attached-picture stream to extract as a thumbnail,
    /// or <see cref="VideoStream"/> to extract a frame from the first video track.
    /// </param>
    Task<BitmapSource?> GetAsync(
        string filePath,
        int streamIndex,
        CancellationToken cancellationToken);
}
