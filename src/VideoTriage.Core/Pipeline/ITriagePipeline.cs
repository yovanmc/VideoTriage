using VideoTriage.Core.Models;

namespace VideoTriage.Core.Pipeline;

public interface ITriagePipeline
{
    Task<TriageSummary> RunAsync(
        string folder,
        IReadOnlyList<string> filePaths,
        TriageOptions options,
        IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null,
        CancellationToken cancellationToken = default);
}
