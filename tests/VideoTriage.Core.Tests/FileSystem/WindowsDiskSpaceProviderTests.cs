using Shouldly;
using VideoTriage.Core.FileSystem;
using Xunit;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class WindowsDiskSpaceProviderTests
{
    private readonly WindowsDiskSpaceProvider _provider = new();

    [Fact]
    public void GetAvailableFreeSpace_LocalTempPath_ReturnsPositiveValue()
    {
        var freeSpace = _provider.GetAvailableFreeSpace(Path.GetTempPath());

        freeSpace.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GetAvailableFreeSpace_FilePath_ReturnsPositiveValue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var freeSpace = _provider.GetAvailableFreeSpace(tempFile);
            freeSpace.ShouldBeGreaterThan(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetAvailableFreeSpace_NonExistentPath_ThrowsIOException()
    {
        var badPath = @"Z:\NonExistentDrive\SubDir";

        Should.Throw<IOException>(() => _provider.GetAvailableFreeSpace(badPath));
    }
}
