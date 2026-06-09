using VideoTriage.Core.State;

namespace VideoTriage.Core.Pipeline;

/// <summary>
/// Thrown when startup recovery finds entries it cannot resolve automatically.
/// The run must not start until these are manually resolved.
/// </summary>
public sealed class ReplacementRecoveryRequiredException(IReadOnlyList<UnrecoverableEntry> entries)
    : InvalidOperationException(
        $"Cannot start: {entries.Count} unrecoverable replacement transaction(s) detected. " +
        "Inspect the replacement journal manually before continuing.")
{
    public IReadOnlyList<UnrecoverableEntry> Entries { get; } = entries;
}
