using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public interface IFfprobeService
{
    Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
