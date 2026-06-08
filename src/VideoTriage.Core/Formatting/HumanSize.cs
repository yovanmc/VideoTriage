using System.Globalization;

namespace VideoTriage.Core.Formatting;

/// <summary>Formats byte counts as human-readable strings (e.g. "1.5 GB").</summary>
public static class HumanSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 B";

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Bytes show no decimal; KB and up show one decimal place.
        return unit == 0
            ? $"{(long)value} {Units[unit]}"
            : $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
