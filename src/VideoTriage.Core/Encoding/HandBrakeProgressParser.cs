using System.Text.Json;

namespace VideoTriage.Core.Encoding;

public static class HandBrakeProgressParser
{
    public static double? TryParseProgress(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("Working", out var working) ||
                !working.TryGetProperty("Progress", out var progress) ||
                !progress.TryGetDouble(out var value))
            {
                return null;
            }

            return Math.Clamp(value, 0, 1);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
