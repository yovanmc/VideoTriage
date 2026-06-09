using Shouldly;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class FileRunLeaseFactoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Acquire_SecondLeaseForSameDataDirectory_ThrowsRunAlreadyActiveException()
    {
        var factory = new FileRunLeaseFactory();
        using var first = factory.Acquire(_tempDir);

        Should.Throw<RunAlreadyActiveException>(() => factory.Acquire(_tempDir));
    }

    [Fact]
    public void Acquire_AfterDispose_CanAcquireAgain()
    {
        var factory = new FileRunLeaseFactory();
        var first = factory.Acquire(_tempDir);
        first.Dispose();

        Should.NotThrow(() =>
        {
            using var second = factory.Acquire(_tempDir);
        });
    }

    [Fact]
    public void Acquire_DifferentDirectories_BothSucceed()
    {
        var dir2 = _tempDir + "_other";
        var factory = new FileRunLeaseFactory();

        Should.NotThrow(() =>
        {
            using var first = factory.Acquire(_tempDir);
            using var second = factory.Acquire(dir2);
        });

        if (Directory.Exists(dir2))
            Directory.Delete(dir2, recursive: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
