using System.Globalization;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public sealed class BppClassifier : IVideoClassifier
{
    public ClassificationResult Classify(VideoStats stats, TriageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stats);
        options ??= new TriageOptions();

        if (stats.Width <= 0
            || stats.Height <= 0
            || stats.FramesPerSecond <= 0
            || stats.Duration <= TimeSpan.Zero
            || stats.EffectiveBitrateBitsPerSecond <= 0)
        {
            return new ClassificationResult
            {
                Outcome = ClassificationOutcome.InvalidMetadata,
                Reason = "Invalid metadata: width, height, frame rate, duration, and bitrate must be positive.",
                Stats = stats
            };
        }

        if (options.SkipAv1 && string.Equals(stats.CodecName, "av1", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassificationResult
            {
                Outcome = ClassificationOutcome.SkipAlreadyAv1,
                Reason = "Skipped because the video is already AV1.",
                Stats = stats
            };
        }

        if (stats.BitsPerPixel < options.CandidateBppThreshold)
        {
            return new ClassificationResult
            {
                Outcome = ClassificationOutcome.SkipLowBpp,
                Reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Skipped because bpp {stats.BitsPerPixel:0.000} is below threshold {options.CandidateBppThreshold:0.000}."),
                Stats = stats
            };
        }

        return new ClassificationResult
        {
            Outcome = ClassificationOutcome.Candidate,
            Reason = string.Create(
                CultureInfo.InvariantCulture,
                $"Candidate because bpp {stats.BitsPerPixel:0.000} is at or above threshold {options.CandidateBppThreshold:0.000}."),
            Stats = stats
        };
    }
}
