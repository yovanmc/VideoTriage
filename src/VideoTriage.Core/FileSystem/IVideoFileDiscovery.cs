using VideoTriage.Core.Models;

namespace VideoTriage.Core.FileSystem;

public interface IVideoFileDiscovery
{
    IEnumerable<string> EnumerateVideos(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<DiscoveryWarning>? warnings = null,
        CancellationToken cancellationToken = default);
}
