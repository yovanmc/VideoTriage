using Shouldly;
using VideoTriage.Core.Tools;
using Xunit;

namespace VideoTriage.Core.Tests.Tools;

public sealed class ToolLocatorTests
{
    [Fact]
    public void ToolLocator_ImplementsIToolLocator()
    {
        IToolLocator locator = new ToolLocator(string.Empty);

        locator.FindOnPath("ffmpeg").ShouldBeNull();
    }

    [Fact]
    public void FindOnPath_FindsExecutableInInjectedPath()
    {
        using var temp = new TempDirectory();
        var toolPath = System.IO.Path.Combine(temp.Path, "ffprobe.exe");
        File.WriteAllText(toolPath, string.Empty);

        var result = new ToolLocator(pathOverride: temp.Path).FindOnPath("ffprobe");

        result.ShouldBe(toolPath);
    }

    [Fact]
    public void FindOnPath_AcceptsExecutableNameWithExeSuffix()
    {
        using var temp = new TempDirectory();
        var toolPath = System.IO.Path.Combine(temp.Path, "ffprobe.exe");
        File.WriteAllText(toolPath, string.Empty);

        var result = new ToolLocator(pathOverride: temp.Path).FindOnPath("ffprobe.exe");

        result.ShouldBe(toolPath);
    }

    [Fact]
    public void FindOnPath_ReturnsNullWhenMissing()
    {
        using var temp = new TempDirectory();

        var result = new ToolLocator(pathOverride: temp.Path).FindOnPath("ffprobe");

        result.ShouldBeNull();
    }

    [Fact]
    public void RequireOnPath_ThrowsWithToolNameAndHintWhenMissing()
    {
        using var temp = new TempDirectory();

        var exception = Should.Throw<FileNotFoundException>(() =>
            new ToolLocator(pathOverride: temp.Path).RequireOnPath("ffprobe"));

        exception.Message.ShouldContain("ffprobe");
        exception.Message.ShouldContain("PATH");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
