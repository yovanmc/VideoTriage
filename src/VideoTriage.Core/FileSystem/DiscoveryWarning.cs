namespace VideoTriage.Core.FileSystem;

public sealed record DiscoveryWarning
{
    public required string DirectoryPath { get; init; }
    public required Exception Exception { get; init; }
}
