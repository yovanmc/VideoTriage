using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public interface IFolderProbeScanner
{
    Task<FolderScanSummary> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default);
}
