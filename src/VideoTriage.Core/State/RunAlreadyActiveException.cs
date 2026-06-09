namespace VideoTriage.Core.State;

/// <summary>
/// Thrown when a run cannot be started because another run is already active in the same folder.
/// </summary>
public sealed class RunAlreadyActiveException(string dataDirectory, Exception? innerException = null)
    : InvalidOperationException(
        $"A VideoTriage run is already active in '{dataDirectory}'. " +
        "Wait for it to complete or remove the run.lock file if the previous run crashed.",
        innerException)
{
    public string DataDirectory { get; } = dataDirectory;
}
