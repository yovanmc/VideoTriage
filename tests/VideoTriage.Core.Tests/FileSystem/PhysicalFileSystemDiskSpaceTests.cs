using Shouldly;
using VideoTriage.Core.FileSystem;
using Xunit;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class PhysicalFileSystemDiskSpaceTests
{
    [Fact]
    public void GetAvailableFreeSpace_DelegatesToProvider()
    {
        const long expectedFreeSpace = 12345L;
        var fake = new FakeDiskSpaceProvider(expectedFreeSpace);
        var fs = new PhysicalFileSystem(fake);

        var result = fs.GetAvailableFreeSpace(@"C:\any\path");

        result.ShouldBe(expectedFreeSpace);
        fake.LastPath.ShouldBe(@"C:\any\path");
    }

    private sealed class FakeDiskSpaceProvider(long freeSpace) : IDiskSpaceProvider
    {
        public string? LastPath { get; private set; }

        public long GetAvailableFreeSpace(string path)
        {
            LastPath = path;
            return freeSpace;
        }
    }
}
