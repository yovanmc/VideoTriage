using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

public interface ICompletedFileStore
{
    IReadOnlyList<CompletedFileEntry> Load();
    void Append(CompletedFileEntry entry);
}
