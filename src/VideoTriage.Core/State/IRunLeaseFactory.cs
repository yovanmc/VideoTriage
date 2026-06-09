namespace VideoTriage.Core.State;

/// <summary>
/// Acquires an exclusive per-folder run lease, preventing concurrent runs on the same folder.
/// </summary>
public interface IRunLeaseFactory
{
    /// <summary>
    /// Acquires an exclusive lock on <paramref name="dataDirectory"/>. Throws
    /// <see cref="RunAlreadyActiveException"/> if the folder is already locked.
    /// Dispose the returned handle to release the lock.
    /// </summary>
    IDisposable Acquire(string dataDirectory);
}
