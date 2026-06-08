namespace VideoTriage.Core.Poster;

public sealed record PosterEmbedResult
{
    public required string OutputPath { get; init; }
    public required bool Embedded { get; init; }
    public required string Reason { get; init; }
}
