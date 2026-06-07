namespace VideoTriage.Core.Tools;

public sealed record ToolLocation
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
}
