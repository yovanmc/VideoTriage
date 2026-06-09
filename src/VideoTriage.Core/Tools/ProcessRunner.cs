using System.Diagnostics;

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

        var stdoutTask = ReadStandardOutputAsync(process, request);
        var stderrTask = HandleStandardErrorAsync(process, stderrPath, request);

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

        var (stdout, truncated) = await stdoutTask;
        await stderrTask;

        stopwatch.Stop();

        return new ProcessResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = stdout,
            StandardOutputTruncated = truncated,
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
        ProcessRequest request)
    {
        if (stderrPath is not null)
        {
            await using var file = new FileStream(
                stderrPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(file);
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                await writer.WriteLineAsync(line);
                if (request.StandardErrorLines is not null)
                {
                    try { request.StandardErrorLines.Report(line); }
                    catch (Exception ex) { request.ProgressCallbackError?.Invoke(ex); }
                }
            }
        }
        else
        {
            // No file requested — drain stderr to prevent process hang on a full buffer.
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (request.StandardErrorLines is not null)
                {
                    try { request.StandardErrorLines.Report(line); }
                    catch (Exception ex) { request.ProgressCallbackError?.Invoke(ex); }
                }
            }
        }
    }

    private static async Task<(string Output, bool Truncated)> ReadStandardOutputAsync(
        Process process,
        ProcessRequest request)
    {
        var buffer = new BoundedTextBuffer(request.StandardOutputLimitCharacters);
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            buffer.Append(line);
            if (request.StandardOutputLines is not null)
            {
                try { request.StandardOutputLines.Report(line); }
                catch (Exception ex) { request.ProgressCallbackError?.Invoke(ex); }
            }
        }
        return (buffer.Build(), buffer.Truncated);
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

/// <summary>
/// Fixed-capacity circular text buffer. Keeps the newest N characters, drops old lines.
/// Thread-safe for concurrent appends.
/// </summary>
internal sealed class BoundedTextBuffer(int maxCharacters)
{
    private readonly Queue<string> _lines = new();
    private readonly object _lock = new();
    private int _totalChars;
    private bool _truncated;

    public bool Truncated => _truncated;

    public void Append(string line)
    {
        lock (_lock)
        {
            _lines.Enqueue(line);
            _totalChars += line.Length + Environment.NewLine.Length;
            while (_totalChars > maxCharacters && _lines.Count > 0)
            {
                var removed = _lines.Dequeue();
                _totalChars -= removed.Length + Environment.NewLine.Length;
                _truncated = true;
            }
        }
    }

    public string Build()
    {
        lock (_lock)
        {
            return string.Join(Environment.NewLine, _lines);
        }
    }
}
