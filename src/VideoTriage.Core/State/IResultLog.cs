using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

public interface IResultLog
{
    void Append(ResultLogEntry entry);
}
