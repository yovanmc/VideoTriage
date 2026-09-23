using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

/// <summary>
/// The ONLY type permitted to call permanent-delete or Recycle Bin APIs.
/// </summary>
public interface IFileRemover
{
    void Remove(string path, DeleteMode mode);
}
