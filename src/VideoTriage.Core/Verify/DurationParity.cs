namespace VideoTriage.Core.Verify;

public static class DurationParity
{
    public static bool WithinTolerance(
        TimeSpan source,
        TimeSpan output,
        double tolerancePercent)
    {
        var sourceSeconds = source.TotalSeconds;
        if (sourceSeconds == 0)
            return output.TotalSeconds == 0;

        var differenceFraction =
            Math.Abs(output.TotalSeconds - sourceSeconds) / sourceSeconds;
        return differenceFraction * 100 <= tolerancePercent;
    }
}
