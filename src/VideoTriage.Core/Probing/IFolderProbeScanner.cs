using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public interface IFolderProbeScanner
{
    Task<IReadOnlyList<ProbeResult>> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default);
}
