using System.Diagnostics;
using System.Text;

namespace VideoTriage.Core.Tools;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("Process file name is required.", nameof(request));

        string? stderrPath = null;
        if (request.StderrDirectory is not null)
        {
            Directory.CreateDirectory(request.StderrDirectory);
            stderrPath = Path.Combine(
                request.StderrDirectory,
                $"videotriage-stderr-{Guid.NewGuid():N}.log");
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request)
        };

        var stopwatch = Stopwatch.StartNew();
        process.Start();

        var stdoutTask = ReadStandardOutputAsync(process, request.StandardOutputLines);
        var stderrTask = HandleStandardErrorAsync(process, stderrPath, request.StandardErrorLines);

        var timedOut = false;
        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await stdoutTask;
            await stderrTask;
            throw;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        await stderrTask;

        stopwatch.Stop();

        return new ProcessResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = stdout,
            StandardErrorPath = stderrPath,
            Elapsed = stopwatch.Elapsed,
            TimedOut = timedOut
        };
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            startInfo.WorkingDirectory = request.WorkingDirectory;

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static async Task HandleStandardErrorAsync(
        Process process,
        string? stderrPath,
        IProgress<string>? stderrLines)
    {
        if (stderrPath is not null)
        {
            await using var file = new FileStream(
                stderrPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(file);
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                await writer.WriteLineAsync(line);
                stderrLines?.Report(line);
            }
        }
        else
        {
            // No file requested — drain stderr to prevent process hang on a full buffer.
            while (await process.StandardError.ReadLineAsync() is { } line)
                stderrLines?.Report(line);
        }
    }

    private static async Task<string> ReadStandardOutputAsync(
        Process process,
        IProgress<string>? progress)
    {
        var output = new StringBuilder();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            progress?.Report(line);
            output.AppendLine(line);
        }

        return output.ToString();
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
