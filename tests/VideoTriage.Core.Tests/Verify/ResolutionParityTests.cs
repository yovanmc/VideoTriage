using Shouldly;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Verify;

public sealed class ResolutionParityTests
{
    [Fact]
    public void Matches_ExactSameDimensions_ReturnsTrue()
    {
        ResolutionParity.Matches(1920, 1080, 1920, 1080, 2).ShouldBeTrue();
    }

    [Fact]
    public void Matches_SwappedWidthHeight_ReturnsTrue()
    {
        ResolutionParity.Matches(1080, 1920, 1920, 1080, 2).ShouldBeTrue();
    }

    [Fact]
    public void Matches_TinyNudgeWithinTolerance_ReturnsTrue()
    {
        ResolutionParity.Matches(1920, 1080, 1918, 1080, 2).ShouldBeTrue();
    }

    [Fact]
    public void Matches_GenuineDownscale_ReturnsFalse()
    {
        ResolutionParity.Matches(1010, 1354, 506, 676, 2).ShouldBeFalse();
    }

    [Fact]
    public void Matches_JustOutsideTolerance_ReturnsFalse()
    {
        ResolutionParity.Matches(1920, 1080, 1862, 1080, 2).ShouldBeFalse();
    }

    [Fact]
    public void Matches_ExactlyAtToleranceBoundary_ReturnsTrue()
    {
        ResolutionParity.Matches(1920, 1080, 1882, 1080, 2).ShouldBeTrue();
    }
}
