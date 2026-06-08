namespace VideoTriage.Core.Models;

public sealed record TriageOptions
{
    public double CandidateBppThreshold { get; init; } = 0.13;
    public bool SkipAv1 { get; init; } = true;
    public string[] VideoExtensions { get; init; } =
    [
        ".mp4",
        ".m4v",
        ".mov",
        ".mkv",
        ".avi",
        ".wmv",
        ".webm"
    ];

    public bool DeepVerify { get; init; } = true;
    public double DurationTolerancePercent { get; init; } = 5;
    public bool RequireResolutionMatch { get; init; } = true;
    public double ResolutionTolerancePercent { get; init; } = 2;
    public bool RequireAudioParity { get; init; } = true;

    // M4 pipeline policy.
    public DeleteMode DeleteMode { get; init; } = DeleteMode.RecycleBin;
    public double MinimumFreeGigabytes { get; init; } = 5;
    public double MarginalThresholdPercent { get; init; } = 10;
    public bool DryRun { get; init; }
}
