namespace VideoTriage.Core.FileSystem;

/// <summary>Real filesystem adapter. Thin pass-through to <see cref="File"/>/<see cref="Directory"/>/<see cref="DriveInfo"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public void MoveFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath);

    public void DeleteFile(string path) => File.Delete(path);

    public long GetAvailableFreeSpace(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new ArgumentException($"Cannot resolve drive root for path: {path}", nameof(path));
        return new DriveInfo(root).AvailableFreeSpace;
    }

    public DateTimeOffset GetLastWriteTimeUtc(string path) =>
        new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
}
