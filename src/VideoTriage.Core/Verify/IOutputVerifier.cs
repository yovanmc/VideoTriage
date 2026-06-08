using VideoTriage.Core.Models;

namespace VideoTriage.Core.Verify;

public interface IOutputVerifier
{
    Task<VerificationResult> VerifyAsync(
        VideoStats source,
        string outputPath,
        TriageOptions options,
        CancellationToken cancellationToken = default);
}
