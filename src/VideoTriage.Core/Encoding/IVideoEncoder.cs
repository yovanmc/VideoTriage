using VideoTriage.Core.Models;

namespace VideoTriage.Core.Encoding;

public interface IVideoEncoder
{
    Task<EncodeResult> EncodeAsync(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
