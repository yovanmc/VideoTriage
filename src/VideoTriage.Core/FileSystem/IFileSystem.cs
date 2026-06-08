namespace VideoTriage.Core.FileSystem;

/// <summary>
/// Filesystem seam so the safety-critical replacement logic can be exercised against an in-memory
/// fake without ever touching real user files.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    long GetFileLength(string path);
    void CreateDirectory(string path);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);
    void MoveFile(string sourcePath, string destinationPath);
    void DeleteFile(string path);
    long GetAvailableFreeSpace(string path);
    DateTimeOffset GetLastWriteTimeUtc(string path);
}
