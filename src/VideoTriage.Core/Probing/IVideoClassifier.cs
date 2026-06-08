using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public interface IVideoClassifier
{
    ClassificationResult Classify(VideoStats stats, TriageOptions? options = null);
}
