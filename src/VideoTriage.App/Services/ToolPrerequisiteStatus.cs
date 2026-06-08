namespace VideoTriage.App.Services;

public sealed record ToolPrerequisiteStatus(
    string Name,
    bool IsAvailable,
    string? FullPath,
    string InstallHint);
