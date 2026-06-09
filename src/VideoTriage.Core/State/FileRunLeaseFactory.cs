namespace VideoTriage.Core.State;

/// <summary>
/// Uses an exclusive FileStream on <c>run.lock</c> inside the data directory to prevent
/// concurrent runs on the same folder.
/// </summary>
public sealed class FileRunLeaseFactory : IRunLeaseFactory
{
    public IDisposable Acquire(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "run.lock");
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new RunAlreadyActiveException(dataDirectory, exception);
        }
    }
}
