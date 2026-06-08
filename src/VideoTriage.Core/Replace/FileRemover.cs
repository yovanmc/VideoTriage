using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

/// <summary>
/// Removes an original after a verified replacement is staged. RecycleBin (recoverable) is the safe
/// default; Permanent is a hard delete used only when the caller explicitly opts in.
/// </summary>
public sealed class FileRemover : IFileRemover
{
    public void Remove(string path, DeleteMode mode)
    {
        switch (mode)
        {
            case DeleteMode.Permanent:
                File.Delete(path);
                break;

            case DeleteMode.RecycleBin:
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown delete mode.");
        }
    }
}
