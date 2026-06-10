using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;

namespace VideoTriage.App.Tests.Fakes;

public sealed class FakeFolderProbeScanner : IFolderProbeScanner
{
    public List<ProbeResult> Results { get; } = [];
    public string? LastFolder { get; private set; }
    public bool? LastRecursive { get; private set; }
    public TaskCompletionSource? BlockUntil { get; set; }

    public async Task<FolderScanSummary> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastFolder = folderPath;
        LastRecursive = recursive;
        foreach (var result in Results)
            progress?.Report(result);

        if (BlockUntil is not null)
            await BlockUntil.Task.WaitAsync(cancellationToken);

        return new FolderScanSummary
        {
            FilesDiscovered = Results.Count,
            CandidateCount = Results.Count(r => r.Classification?.Outcome == ClassificationOutcome.Candidate),
            ProbeFailureCount = Results.Count(r => r.Failure is not null),
        };
    }
}
