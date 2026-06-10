namespace VideoTriage.Core.FileSystem;

/// <summary>Real filesystem adapter. Thin pass-through to <see cref="File"/>/<see cref="Directory"/>/<see cref="DriveInfo"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    private readonly IDiskSpaceProvider _diskSpace;

    public PhysicalFileSystem() : this(new WindowsDiskSpaceProvider()) { }

    public PhysicalFileSystem(IDiskSpaceProvider diskSpace)
    {
        _diskSpace = diskSpace ?? throw new ArgumentNullException(nameof(diskSpace));
    }

    public bool FileExists(string path) => File.Exists(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public void MoveFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath);

    public void DeleteFile(string path) => File.Delete(path);

    public long GetAvailableFreeSpace(string path) => _diskSpace.GetAvailableFreeSpace(path);

    public DateTimeOffset GetLastWriteTimeUtc(string path) =>
        new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
}
