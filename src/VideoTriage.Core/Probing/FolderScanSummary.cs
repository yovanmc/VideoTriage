namespace VideoTriage.Core.Probing;

public sealed record FolderScanSummary
{
    public required int FilesDiscovered { get; init; }
    public required int CandidateCount { get; init; }
    public required int ProbeFailureCount { get; init; }
}
