using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using Xunit;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class VideoFileDiscoveryTests
{
    [Fact]
    public void FindVideos_FindsDefaultVideoExtensions()
    {
        using var temp = new TempDirectory();
        temp.File("a.mp4");
        temp.File("b.MOV");
        temp.File("notes.txt");

        var results = new VideoFileDiscovery().FindVideos(temp.Path);

        results.Select(Path.GetFileName).ShouldBe(new[] { "a.mp4", "b.MOV" });
    }

    [Fact]
    public void FindVideos_HonorsCustomExtensionList()
    {
        using var temp = new TempDirectory();
        temp.File("a.custom");
        temp.File("b.mp4");

        var results = new VideoFileDiscovery().FindVideos(
            temp.Path,
            new TriageOptions { VideoExtensions = [".custom"] });

        results.Select(Path.GetFileName).ShouldBe(new[] { "a.custom" });
    }

    [Fact]
    public void FindVideos_RecursiveFalseIgnoresNestedFiles()
    {
        using var temp = new TempDirectory();
        temp.File("root.mp4");
        temp.File(Path.Combine("nested", "child.mp4"));

        var results = new VideoFileDiscovery().FindVideos(temp.Path, recursive: false);

        results.Select(Path.GetFileName).ShouldBe(new[] { "root.mp4" });
    }

    [Fact]
    public void FindVideos_RecursiveTrueIncludesNestedFiles()
    {
        using var temp = new TempDirectory();
        temp.File("root.mp4");
        temp.File(Path.Combine("nested", "child.mp4"));

        var results = new VideoFileDiscovery().FindVideos(temp.Path, recursive: true);

        results.Select(Path.GetFileName).ShouldBe(new[] { "child.mp4", "root.mp4" }, ignoreOrder: false);
    }

    [Fact]
    public void FindVideos_IgnoresVideoTriageTempFiles()
    {
        using var temp = new TempDirectory();
        temp.File("keep.mp4");
        temp.File("skip.videotriage.tmp.mp4");
        temp.File("skip.videotriage.partial.mp4");

        var results = new VideoFileDiscovery().FindVideos(temp.Path);

        results.Select(Path.GetFileName).ShouldBe(new[] { "keep.mp4" });
    }

    [Fact]
    public void FindVideos_ThrowsWhenFolderDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Should.Throw<DirectoryNotFoundException>(() =>
            new VideoFileDiscovery().FindVideos(missing));
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
