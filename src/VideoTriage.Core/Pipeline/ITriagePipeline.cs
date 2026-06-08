using VideoTriage.Core.Models;

namespace VideoTriage.Core.Pipeline;

public interface ITriagePipeline
{
    Task<TriageSummary> RunAsync(
        string folder,
        TriageOptions options,
        bool recursive = false,
        IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null,
        CancellationToken cancellationToken = default);
}
