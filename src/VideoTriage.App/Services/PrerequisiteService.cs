using VideoTriage.Core.Tools;

namespace VideoTriage.App.Services;

public sealed class PrerequisiteService(IToolLocator locator) : IPrerequisiteService
{
    public IReadOnlyList<ToolPrerequisiteStatus> Check() =>
    [
        Status("ffprobe", "winget install Gyan.FFmpeg"),
        Status("ffmpeg", "winget install Gyan.FFmpeg"),
        Status("HandBrakeCLI", "winget install HandBrake.HandBrake.CLI")
    ];

    private ToolPrerequisiteStatus Status(string name, string installHint)
    {
        var path = locator.FindOnPath(name);
        return new ToolPrerequisiteStatus(name, path is not null, path, installHint);
    }
}
