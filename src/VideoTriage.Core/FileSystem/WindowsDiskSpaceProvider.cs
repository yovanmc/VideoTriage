using System.Runtime.InteropServices;

namespace VideoTriage.Core.FileSystem;

public sealed class WindowsDiskSpaceProvider : IDiskSpaceProvider
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    public long GetAvailableFreeSpace(string path)
    {
        // Resolve to a directory; GetDiskFreeSpaceExW needs a directory path.
        var dir = File.Exists(path)
            ? Path.GetDirectoryName(Path.GetFullPath(path)) ?? path
            : path;

        // UNC roots (\\server\share) require a trailing backslash.
        if (!dir.EndsWith(Path.DirectorySeparatorChar))
            dir += Path.DirectorySeparatorChar;

        if (!GetDiskFreeSpaceExW(dir, out var freeBytesAvailable, out _, out _))
        {
            var error = Marshal.GetLastWin32Error();
            throw new IOException($"GetDiskFreeSpaceExW failed for '{dir}' (Win32 error {error}).");
        }

        return (long)freeBytesAvailable;
    }
}
