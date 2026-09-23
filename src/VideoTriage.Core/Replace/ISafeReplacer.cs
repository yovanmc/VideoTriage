using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

/// <summary>
/// Crash-safe replacement of an original by a smaller, already-verified candidate. The only type
/// permitted to request removal of an original.
/// </summary>
public interface ISafeReplacer
{
    ReplaceResult Replace(string originalPath, string verifiedReplacementPath, DeleteMode deleteMode);
}
