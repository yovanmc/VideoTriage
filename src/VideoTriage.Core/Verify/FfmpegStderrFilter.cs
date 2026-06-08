using System.Text.RegularExpressions;

namespace VideoTriage.Core.Verify;

public static class FfmpegStderrFilter
{
    private static readonly Regex BenignDts = new(
        @"non.?monotonically increasing dts",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BenignDroppedDts = new(
        @"^DTS .+ invalid dropping$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BenignRepeat = new(
        @"Last message repeated \d+ times",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> RealErrorLines(string stderrText)
    {
        if (string.IsNullOrWhiteSpace(stderrText))
            return [];

        var lines = stderrText.Split(["\r\n", "\n"], StringSplitOptions.None);
        var errors = new List<string>();

        foreach (var line in lines)
        {
            if (RealErrorLine(line) is { } error)
                errors.Add(error);
        }

        return errors;
    }

    public static string? FirstRealErrorLine(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (RealErrorLine(line) is { } error)
                return error;
        }

        return null;
    }

    private static string? RealErrorLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return null;
        if (BenignDts.IsMatch(trimmed))
            return null;
        if (BenignDroppedDts.IsMatch(trimmed))
            return null;
        if (BenignRepeat.IsMatch(trimmed))
            return null;

        return trimmed;
    }
}
