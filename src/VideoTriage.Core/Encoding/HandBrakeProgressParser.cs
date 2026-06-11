using System.Text.Json;

namespace VideoTriage.Core.Encoding;

public sealed record HandBrakeProgress(double Progress, int? EtaSeconds);

public static class HandBrakeProgressParser
{
    private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true };

    /// <summary>Parses one complete HandBrake --json object. Returns null unless it is a WORKING progress object.</summary>
    public static HandBrakeProgress? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            if (!document.RootElement.TryGetProperty("Working", out var working) ||
                !working.TryGetProperty("Progress", out var progress) ||
                !progress.TryGetDouble(out var value))
            {
                return null;
            }

            int? eta = null;
            if (working.TryGetProperty("ETASeconds", out var etaEl) &&
                etaEl.TryGetInt32(out var etaValue) && etaValue >= 0)
            {
                eta = etaValue;
            }

            return new HandBrakeProgress(Math.Clamp(value, 0, 1), eta);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
