using System.IO;
using VideoTriage.Core.Models;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Probing;

public sealed class FfprobeService : IFfprobeService
{
    private readonly string _ffprobePath;
    private readonly IProcessRunner _processRunner;
    private readonly FfprobeJsonParser _parser;

    public FfprobeService(string ffprobePath, IProcessRunner processRunner, FfprobeJsonParser parser)
    {
        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            throw new ArgumentException("ffprobe path is required.", nameof(ffprobePath));
        }

        _ffprobePath = ffprobePath;
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Failure(filePath, $"File does not exist: {filePath}");
        }

        var processResult = await _processRunner.RunAsync(new ProcessRequest
        {
            FileName = _ffprobePath,
            Arguments = ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", filePath],
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken);

        if (!processResult.Succeeded)
        {
            return Failure(
                filePath,
                $"ffprobe failed with exit code {processResult.ExitCode}.",
                processResult.ExitCode,
                processResult.StandardErrorPath);
        }

        try
        {
            var fileSizeBytes = new FileInfo(filePath).Length;
            var stats = _parser.Parse(filePath, fileSizeBytes, processResult.StandardOutput);
            return new ProbeResult
            {
                FilePath = filePath,
                Stats = stats
            };
        }
        catch (InvalidDataException exception)
        {
            return Failure(filePath, exception.Message, stderrPath: processResult.StandardErrorPath);
        }
    }

    private static ProbeResult Failure(string filePath, string message, int? exitCode = null, string? stderrPath = null) =>
        new()
        {
            FilePath = filePath,
            Failure = new ProbeFailure
            {
                FilePath = filePath,
                Message = message,
                ExitCode = exitCode,
                StderrPath = stderrPath
            }
        };
}
