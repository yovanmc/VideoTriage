namespace VideoTriage.Core.Tools;

public sealed class ToolLocator
{
    private readonly string? _pathOverride;

    public ToolLocator(string? pathOverride = null)
    {
        _pathOverride = pathOverride;
    }

    public string? FindOnPath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            throw new ArgumentException("Executable name is required.", nameof(executableName));
        }

        foreach (var directory in GetPathDirectories())
        {
            foreach (var candidateName in GetCandidateNames(executableName))
            {
                var candidatePath = Path.Combine(directory, candidateName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }

    public ToolLocation RequireOnPath(string executableName)
    {
        var fullPath = FindOnPath(executableName);
        if (fullPath is null)
        {
            throw new FileNotFoundException(
                $"Required tool '{executableName}' was not found on PATH. Install ffmpeg/ffprobe and make sure the executable directory is on PATH.");
        }

        return new ToolLocation
        {
            Name = executableName,
            FullPath = fullPath
        };
    }

    private IEnumerable<string> GetPathDirectories()
    {
        var path = _pathOverride ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists);
    }

    private static IEnumerable<string> GetCandidateNames(string executableName)
    {
        yield return executableName;

        if (OperatingSystem.IsWindows()
            && !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{executableName}.exe";
        }
    }
}
