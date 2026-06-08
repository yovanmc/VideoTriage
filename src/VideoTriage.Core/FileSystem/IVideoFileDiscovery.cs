using VideoTriage.Core.Models;

namespace VideoTriage.Core.FileSystem;

public interface IVideoFileDiscovery
{
    IReadOnlyList<string> FindVideos(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false);
}
