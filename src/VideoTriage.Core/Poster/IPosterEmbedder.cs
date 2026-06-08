using VideoTriage.Core.Models;

namespace VideoTriage.Core.Poster;

public interface IPosterEmbedder
{
    Task<PosterEmbedResult> EmbedAsync(
        string verifiedEncodePath,
        VideoStats source,
        TriageOptions options,
        CancellationToken cancellationToken = default);
}
