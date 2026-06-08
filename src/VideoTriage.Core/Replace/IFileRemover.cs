using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

/// <summary>
/// The ONLY type permitted to call permanent-delete or Recycle Bin APIs (architecture contract §1.4).
/// </summary>
public interface IFileRemover
{
    void Remove(string path, DeleteMode mode);
}
