using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.Core.Tools;

namespace VideoTriage.App.Tests.Services;

public sealed class PrerequisiteServiceTests
{
    [Fact]
    public void Check_ReturnsAllRequiredToolsInStableOrder()
    {
        var locator = new FakeLocator(new Dictionary<string, string?>
        {
            ["ffprobe"] = @"C:\tools\ffprobe.exe",
            ["ffmpeg"] = null,
            ["HandBrakeCLI"] = @"C:\tools\HandBrakeCLI.exe"
        });

        var result = new PrerequisiteService(locator).Check();

        result.Select(x => x.Name).ShouldBe(["ffprobe", "ffmpeg", "HandBrakeCLI"]);
        result[0].ShouldBe(new ToolPrerequisiteStatus(
            "ffprobe", true, @"C:\tools\ffprobe.exe", "winget install Gyan.FFmpeg"));
        result[1].IsAvailable.ShouldBeFalse();
        result[1].FullPath.ShouldBeNull();
        result[1].InstallHint.ShouldBe("winget install Gyan.FFmpeg");
    }

    [Fact]
    public void Check_HandBrakeMissing_ReturnsCliSpecificInstallHint()
    {
        var locator = new FakeLocator(new Dictionary<string, string?>());

        var result = new PrerequisiteService(locator).Check();

        result.Single(x => x.Name == "HandBrakeCLI").InstallHint
            .ShouldBe("winget install HandBrake.HandBrake.CLI");
    }

    private sealed class FakeLocator(IReadOnlyDictionary<string, string?> paths) : IToolLocator
    {
        public string? FindOnPath(string executableName) =>
            paths.TryGetValue(executableName, out var path) ? path : null;

        public ToolLocation RequireOnPath(string executableName) =>
            throw new NotSupportedException();
    }
}
