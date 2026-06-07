namespace VideoTriage.Core.Models;

public sealed record VideoStats
{
    public required string FilePath { get; init; }
    public required string CodecName { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double FramesPerSecond { get; init; }
    public required TimeSpan Duration { get; init; }
    public required long FileSizeBytes { get; init; }
    public long? VideoBitrateBitsPerSecond { get; init; }
    public long? ContainerBitrateBitsPerSecond { get; init; }
    public bool HasAudio { get; init; }

    public long EffectiveBitrateBitsPerSecond =>
        VideoBitrateBitsPerSecond
        ?? ContainerBitrateBitsPerSecond
        ?? (Duration.TotalSeconds > 0
            ? (long)Math.Round(FileSizeBytes * 8d / Duration.TotalSeconds)
            : 0);

    public double BitsPerPixel =>
        Width > 0 && Height > 0 && FramesPerSecond > 0 && EffectiveBitrateBitsPerSecond > 0
            ? EffectiveBitrateBitsPerSecond / (Width * Height * FramesPerSecond)
            : 0;
}
