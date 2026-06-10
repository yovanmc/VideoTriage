namespace VideoTriage.Core.FileSystem;

public interface IDiskSpaceProvider
{
    long GetAvailableFreeSpace(string path);
}
