using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public sealed class FolderProbeScanner
{
    private readonly VideoFileDiscovery _discovery;
    private readonly IFfprobeService _ffprobeService;
    private readonly BppClassifier _classifier;

    public FolderProbeScanner(
        VideoFileDiscovery discovery,
        IFfprobeService ffprobeService,
        BppClassifier classifier)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _ffprobeService = ffprobeService ?? throw new ArgumentNullException(nameof(ffprobeService));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public async Task<IReadOnlyList<ProbeResult>> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TriageOptions();
        var results = new List<ProbeResult>();

        foreach (var filePath in _discovery.FindVideos(folderPath, options, recursive))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var probeResult = await _ffprobeService.ProbeAsync(filePath, cancellationToken);
            var completedResult = probeResult.Stats is null
                ? probeResult
                : probeResult with { Classification = _classifier.Classify(probeResult.Stats, options) };

            results.Add(completedResult);
            progress?.Report(completedResult);
        }

        return results;
    }
}
