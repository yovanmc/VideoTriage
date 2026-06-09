using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Verify;

public sealed class OutputVerifier(
    string ffmpegPath,
    IProcessRunner runner,
    IFfprobeService ffprobe) : IOutputVerifier
{
    public async Task<VerificationResult> VerifyAsync(
        VideoStats source,
        string outputPath,
        TriageOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return Fail(
                VerificationOutcome.MissingOrEmpty,
                $"Output file is missing or empty: {outputPath}");
        }

        var probeResult = await ffprobe.ProbeAsync(outputPath, cancellationToken);
        if (!probeResult.Succeeded || probeResult.Stats is null)
        {
            var message = probeResult.Failure?.Message ?? "ffprobe returned no video stats";
            return Fail(
                VerificationOutcome.ProbeFailed,
                $"Output probe failed: {message}");
        }

        var outputStats = probeResult.Stats;

        if (!DurationParity.WithinTolerance(
                source.Duration,
                outputStats.Duration,
                options.DurationTolerancePercent))
        {
            return Fail(
                VerificationOutcome.DurationMismatch,
                $"Duration mismatch: source={source.Duration.TotalSeconds:0.##}s " +
                $"output={outputStats.Duration.TotalSeconds:0.##}s " +
                $"tolerance={options.DurationTolerancePercent}%");
        }

        if (options.RequireResolutionMatch &&
            !ResolutionParity.Matches(
                source.Width,
                source.Height,
                outputStats.Width,
                outputStats.Height,
                options.ResolutionTolerancePercent))
        {
            return Fail(
                VerificationOutcome.ResolutionMismatch,
                $"Resolution mismatch: source={source.Width}x{source.Height} " +
                $"output={outputStats.Width}x{outputStats.Height} " +
                $"tolerance={options.ResolutionTolerancePercent}%");
        }

        if (options.RequireAudioParity && source.HasAudio && !outputStats.HasAudio)
        {
            return Fail(
                VerificationOutcome.AudioMissing,
                "Output is missing audio that was present in the source.");
        }

        if (options.DeepVerify)
        {
            var decodeError = await RunDeepDecodeAsync(outputPath, cancellationToken);
            if (decodeError is not null)
            {
                return Fail(
                    VerificationOutcome.DecodeError,
                    $"Deep decode found errors: {decodeError}");
            }
        }

        return new VerificationResult
        {
            Outcome = VerificationOutcome.Valid,
            Reason = "All verification checks passed.",
            OutputStats = outputStats
        };
    }

    private async Task<string?> RunDeepDecodeAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        var processResult = await runner.RunAsync(
            new ProcessRequest
            {
                FileName = ffmpegPath,
                Arguments = ["-nostdin", "-v", "error", "-i", outputPath, "-f", "null", "-"],
                StderrDirectory = Path.GetTempPath(),
                Timeout = TimeSpan.FromMinutes(5)
            },
            cancellationToken);

        try
        {
            var firstError = File.Exists(processResult.StandardErrorPath)
                ? FfmpegStderrFilter.FirstRealErrorLine(
                    File.ReadLines(processResult.StandardErrorPath))
                : null;

            if (firstError is not null)
                return firstError;

            return processResult.Succeeded
                ? null
                : $"ffmpeg exited {processResult.ExitCode}";
        }
        finally
        {
            DeleteStderrFile(processResult.StandardErrorPath);
        }
    }

    private static VerificationResult Fail(
        VerificationOutcome outcome,
        string reason) =>
        new() { Outcome = outcome, Reason = reason };

    private static void DeleteStderrFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
