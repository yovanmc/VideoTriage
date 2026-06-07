using VideoTriage.Core.Models;

namespace VideoTriage.Core.FileSystem;

public sealed class VideoFileDiscovery
{
    public IReadOnlyList<string> FindVideos(string folderPath, TriageOptions? options = null, bool recursive = false)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder does not exist: {folderPath}");
        }

        options ??= new TriageOptions();
        var extensions = options.VideoExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return Directory
            .EnumerateFiles(folderPath, "*", searchOption)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => !Path.GetFileName(path).Contains(".videotriage.tmp.", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Contains(".videotriage.partial.", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
