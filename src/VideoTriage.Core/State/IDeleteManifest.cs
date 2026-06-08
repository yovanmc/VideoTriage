using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

public interface IDeleteManifest
{
    void Append(DeleteManifestEntry entry);
}
