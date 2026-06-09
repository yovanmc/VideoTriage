using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

/// <summary>
/// Persists the active run's current position so a crash can be detected and surfaced to the user.
/// Cleared only after the pipeline returns a complete summary.
/// </summary>
public interface IActiveRunJournal
{
    void Save(ActiveRunState state);
    void Clear();
    ActiveRunState? Load();
}

public sealed record ActiveRunState
{
    public required Guid RunId { get; init; }
    public required string Folder { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public string? CurrentFile { get; init; }
    public TriagePhase? CurrentPhase { get; init; }
    public required int CompletedFiles { get; init; }
    public required int TotalFiles { get; init; }
}
