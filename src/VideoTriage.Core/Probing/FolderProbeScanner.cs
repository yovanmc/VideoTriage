using System.Threading.Channels;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public sealed class FolderProbeScanner : IFolderProbeScanner
{
    private readonly IVideoFileDiscovery _discovery;
    private readonly IFfprobeService _ffprobeService;
    private readonly IVideoClassifier _classifier;
    private readonly int _maxParallelism;

    public FolderProbeScanner(
        IVideoFileDiscovery discovery,
        IFfprobeService ffprobeService,
        IVideoClassifier classifier,
        int maxParallelism = 4)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _ffprobeService = ffprobeService ?? throw new ArgumentNullException(nameof(ffprobeService));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _maxParallelism = maxParallelism > 0 ? maxParallelism : throw new ArgumentOutOfRangeException(nameof(maxParallelism));
    }

    public async Task<FolderScanSummary> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TriageOptions();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });

        int filesDiscovered = 0;
        int candidates = 0;
        int failures = 0;

        // Producer: enumerate files, write into channel
        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var file in _discovery.EnumerateVideos(folderPath, options, recursive, cancellationToken: cancellationToken))
                {
                    Interlocked.Increment(ref filesDiscovered);
                    await channel.Writer.WriteAsync(file, cancellationToken);
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        // Consumers: probe files in parallel, classify, report
        var workers = Enumerable.Range(0, _maxParallelism).Select(_ => Task.Run(async () =>
        {
            await foreach (var file in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var result = await _ffprobeService.ProbeAsync(file, cancellationToken);
                var classified = result.Stats is null
                    ? result
                    : result with { Classification = _classifier.Classify(result.Stats, options) };

                if (classified.Failure is not null)
                    Interlocked.Increment(ref failures);
                else if (classified.Classification?.Outcome == ClassificationOutcome.Candidate)
                    Interlocked.Increment(ref candidates);

                progress?.Report(classified);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll([producer, .. workers]);

        return new FolderScanSummary
        {
            FilesDiscovered = filesDiscovered,
            CandidateCount = candidates,
            ProbeFailureCount = failures,
        };
    }
}
