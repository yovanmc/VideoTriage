using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

/// <summary>
/// Executes a crash-safe, durably journaled replacement transaction.
/// </summary>
public interface IReplacementTransactionCoordinator
{
    ReplaceResult Replace(ReplacementTransactionRequest request);
}

public sealed record ReplacementTransactionRequest
{
    public required Guid RunId { get; init; }
    public required string OriginalPath { get; init; }
    public required string VerifiedReplacementPath { get; init; }
    public required long OriginalBytes { get; init; }
    public required long ReplacementBytes { get; init; }
    public required DeleteMode DeleteMode { get; init; }
}
