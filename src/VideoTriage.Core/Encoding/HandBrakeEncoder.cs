using VideoTriage.Core.Models;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Encoding;

public sealed class HandBrakeEncoder(
    string handBrakePath,
    IProcessRunner processRunner,
    string presetFilePath,
    string presetName) : IVideoEncoder
{
    public async Task<EncodeResult> EncodeAsync(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var accumulator = new HandBrakeProgressAccumulator();
        var outputLines = new InlineProgress<string>(line =>
        {
            var update = accumulator.Append(line);
            if (update is not null)
                progress?.Report(update.Progress);
        });

        try
        {
            var result = await processRunner.RunAsync(
                new ProcessRequest
                {
                    FileName = handBrakePath,
                    Arguments =
                    [
                        "--preset-import-file",
                        presetFilePath,
                        "-Z",
                        presetName,
                        "-i",
                        inputPath,
                        "-o",
                        outputPath,
                        "--json"
                    ],
                    Timeout = Timeout.InfiniteTimeSpan,
                    StandardOutputLines = outputLines,
                    StandardErrorLines = outputLines   // HandBrake may route --json progress to stderr
                },
                cancellationToken);

            return new EncodeResult
            {
                Outcome = result.Succeeded
                    ? EncodeOutcome.Succeeded
                    : EncodeOutcome.Failed,
                OutputPath = outputPath,
                Reason = result.Succeeded
                    ? "Encode completed."
                    : $"HandBrake exited {result.ExitCode}.",
                ExitCode = result.ExitCode,
                StderrPath = result.StandardErrorPath,
                Elapsed = result.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            return new EncodeResult
            {
                Outcome = EncodeOutcome.Cancelled,
                OutputPath = outputPath,
                Reason = "Encode cancelled."
            };
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
