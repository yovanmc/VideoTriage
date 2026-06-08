using VideoTriage.Core.Models;

namespace VideoTriage.App.Models;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public double CandidateBppThreshold { get; init; } = 0.13;
    public DeleteMode DeleteMode { get; init; } = DeleteMode.RecycleBin;
    public bool DeepVerify { get; init; } = true;
    public bool EmbedPoster { get; init; } = true;
    public double MinimumFreeGigabytes { get; init; } = 5;
    public bool DryRun { get; init; }

    public TriageOptions ToTriageOptions() => new()
    {
        CandidateBppThreshold = CandidateBppThreshold,
        DeleteMode = DeleteMode,
        DeepVerify = DeepVerify,
        EmbedPoster = EmbedPoster,
        MinimumFreeGigabytes = MinimumFreeGigabytes,
        DryRun = DryRun
    };
}
