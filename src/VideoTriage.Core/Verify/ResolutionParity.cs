namespace VideoTriage.Core.Verify;

public static class ResolutionParity
{
    public static bool Matches(int srcW, int srcH, int outW, int outH, double tolerancePercent)
    {
        var (srcMin, srcMaj) = srcW <= srcH ? (srcW, srcH) : (srcH, srcW);
        var (outMin, outMaj) = outW <= outH ? (outW, outH) : (outH, outW);

        return WithinTolerance(srcMin, outMin, tolerancePercent)
            && WithinTolerance(srcMaj, outMaj, tolerancePercent);
    }

    private static bool WithinTolerance(int source, int output, double tolerancePercent)
    {
        if (source == 0)
            return output == 0;

        var differenceFraction = Math.Abs(output - source) / (double)source;
        return differenceFraction * 100 <= tolerancePercent;
    }
}
