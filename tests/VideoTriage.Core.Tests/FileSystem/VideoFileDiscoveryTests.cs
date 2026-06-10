using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using Xunit;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class VideoFileDiscoveryTests
{
    private readonly FakeDirectoryWalker _walker = new();
    private readonly VideoFileDiscovery _discovery;

    public VideoFileDiscoveryTests()
    {
        _discovery = new VideoFileDiscovery(_walker);
    }

    [Fact]
    public void EnumerateVideos_FindsDefaultVideoExtensions()
    {
        using var temp = new TempDirectory();
        temp.File("a.mp4");
        temp.File("b.MOV");
        temp.File("notes.txt");

        var results = new VideoFileDiscovery().EnumerateVideos(temp.Path).ToArray();

        results.Select(Path.GetFileName).ShouldBe(new[] { "a.mp4", "b.MOV" }, ignoreOrder: true);
    }

    [Fact]
    public void EnumerateVideos_HonorsCustomExtensionList()
    {
        using var temp = new TempDirectory();
        temp.File("a.custom");
        temp.File("b.mp4");

        var results = new VideoFileDiscovery().EnumerateVideos(
            temp.Path,
            new TriageOptions { VideoExtensions = [".custom"] }).ToArray();

        results.Select(Path.GetFileName).ShouldBe(new[] { "a.custom" });
    }

    [Fact]
    public void EnumerateVideos_RecursiveFalseIgnoresNestedFiles()
    {
        using var temp = new TempDirectory();
        temp.File("root.mp4");
        temp.File(Path.Combine("nested", "child.mp4"));

        var results = new VideoFileDiscovery().EnumerateVideos(temp.Path, recursive: false).ToArray();

        results.Select(Path.GetFileName).ShouldBe(new[] { "root.mp4" });
    }

    [Fact]
    public void EnumerateVideos_RecursiveTrueIncludesNestedFiles()
    {
        using var temp = new TempDirectory();
        temp.File("root.mp4");
        temp.File(Path.Combine("nested", "child.mp4"));

        var results = new VideoFileDiscovery().EnumerateVideos(temp.Path, recursive: true).ToArray();

        results.Select(Path.GetFileName).ShouldBe(new[] { "child.mp4", "root.mp4" }, ignoreOrder: true);
    }

    [Fact]
    public void EnumerateVideos_IgnoresVideoTriageTempFiles()
    {
        using var temp = new TempDirectory();
        temp.File("keep.mp4");
        temp.File("skip.videotriage.tmp.mp4");
        temp.File("skip.videotriage.partial.mp4");

        var results = new VideoFileDiscovery().EnumerateVideos(temp.Path).ToArray();

        results.Select(Path.GetFileName).ShouldBe(new[] { "keep.mp4" });
    }

    [Fact]
    public void EnumerateVideos_ThrowsWhenFolderDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Should.Throw<DirectoryNotFoundException>(() =>
            new VideoFileDiscovery().EnumerateVideos(missing).ToArray());
    }

    // --- New tests using FakeDirectoryWalker ---

    [Fact]
    public void EnumerateVideos_Recursive_SkipsReparsePointDirectories()
    {
        _walker.FilesByDirectory[@"C:\root"] = [@"C:\root\keep.mp4"];
        _walker.Children.Add(new DirectoryEntry(@"C:\root\link",
            FileAttributes.Directory | FileAttributes.ReparsePoint));
        _walker.FilesByDirectory[@"C:\root\link"] = [@"C:\outside\escape.mp4"];

        var files = _discovery.EnumerateVideos(@"C:\root", recursive: true).ToArray();

        files.ShouldBe([@"C:\root\keep.mp4"]);
    }

    [Fact]
    public void EnumerateVideos_InaccessibleChild_ReportsWarningAndContinues()
    {
        _walker.Children.Add(new DirectoryEntry(@"C:\root\denied", FileAttributes.Directory));
        _walker.Exceptions[@"C:\root\denied"] = new UnauthorizedAccessException("denied");
        _walker.FilesByDirectory[@"C:\root"] = [@"C:\root\keep.mp4"];
        var warnings = new List<DiscoveryWarning>();

        var files = _discovery.EnumerateVideos(
            @"C:\root",
            recursive: true,
            warnings: new InlineProgress<DiscoveryWarning>(warnings.Add)).ToArray();

        files.ShouldBe([@"C:\root\keep.mp4"]);
        warnings.Single().DirectoryPath.ShouldBe(@"C:\root\denied");
    }

    [Fact]
    public void EnumerateVideos_ReturnedPathsRemainUnderSelectedRoot()
    {
        _walker.FilesByDirectory[@"C:\root"] = [@"C:\root\..\outside\escape.mp4"];

        var files = _discovery.EnumerateVideos(@"C:\root").ToArray();

        files.ShouldBeEmpty();
    }

    private sealed class FakeDirectoryWalker : IDirectoryWalker
    {
        public Dictionary<string, List<string>> FilesByDirectory { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<DirectoryEntry> Children { get; } = [];
        public Dictionary<string, Exception> Exceptions { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => true;

        public IEnumerable<string> GetFiles(string directory)
        {
            if (FilesByDirectory.TryGetValue(directory, out var files))
                return files;
            return []; // empty for unmapped dirs (simulates empty directory)
        }

        public IEnumerable<DirectoryEntry> GetDirectories(string directory)
        {
            if (Exceptions.TryGetValue(directory, out var ex)) throw ex;
            return Children;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void File(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            System.IO.File.WriteAllText(fullPath, string.Empty);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
