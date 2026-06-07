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
}
