using VideoTriage.Core.Models;

namespace VideoTriage.Core.FileSystem;

public interface IDirectoryWalker
{
    bool DirectoryExists(string path);
    IEnumerable<string> GetFiles(string directory);
    IEnumerable<DirectoryEntry> GetDirectories(string directory);
}

public sealed record DirectoryEntry(string Path, FileAttributes Attributes);

public sealed class VideoFileDiscovery(IDirectoryWalker? walker = null) : IVideoFileDiscovery
{
    private readonly IDirectoryWalker _walker = walker ?? new PhysicalDirectoryWalker();

    public IEnumerable<string> EnumerateVideos(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<DiscoveryWarning>? warnings = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(folderPath);
        if (!_walker.DirectoryExists(root))
            throw new DirectoryNotFoundException($"Folder does not exist: {root}");

        options ??= new TriageOptions();
        var extensions = options.VideoExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rootPrefix = root + Path.DirectorySeparatorChar;

        bool IsSafe(string file)
        {
            var full = Path.GetFullPath(file);
            return full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
        }

        bool IsVideo(string file) =>
            extensions.Contains(Path.GetExtension(file)) &&
            !TempFileNaming.IsTempArtifact(file);

        if (!recursive)
        {
            foreach (var file in _walker.GetFiles(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsVideo(file) && IsSafe(file))
                    yield return Path.GetFullPath(file);
            }
            yield break;
        }

        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            foreach (var file in _walker.GetFiles(dir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsVideo(file) && IsSafe(file))
                    yield return Path.GetFullPath(file);
            }

            try
            {
                foreach (var entry in _walker.GetDirectories(dir))
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;  // skip symlinks / junctions
                    stack.Push(entry.Path);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                warnings?.Report(new DiscoveryWarning { DirectoryPath = dir, Exception = ex });
            }
            catch (IOException ex)
            {
                warnings?.Report(new DiscoveryWarning { DirectoryPath = dir, Exception = ex });
            }
        }
    }

    private sealed class PhysicalDirectoryWalker : IDirectoryWalker
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public IEnumerable<string> GetFiles(string directory) =>
            Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);

        public IEnumerable<DirectoryEntry> GetDirectories(string directory) =>
            Directory.EnumerateDirectories(directory)
                .Select(d => new DirectoryEntry(d, File.GetAttributes(d)));
    }
}
