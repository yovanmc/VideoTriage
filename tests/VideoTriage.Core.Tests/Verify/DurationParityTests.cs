using Shouldly;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Verify;

public sealed class DurationParityTests
{
    [Fact]
    public void WithinTolerance_EqualDurations_ReturnsTrue()
    {
        DurationParity.WithinTolerance(
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(120),
            5).ShouldBeTrue();
    }

    [Fact]
    public void WithinTolerance_DifferenceWithinTolerance_ReturnsTrue()
    {
        DurationParity.WithinTolerance(
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(124),
            5).ShouldBeTrue();
    }

    [Fact]
    public void WithinTolerance_DifferenceOutsideTolerance_ReturnsFalse()
    {
        DurationParity.WithinTolerance(
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(128),
            5).ShouldBeFalse();
    }

    [Fact]
    public void WithinTolerance_ZeroSourceDuration_ReturnsTrueForZeroOutput()
    {
        DurationParity.WithinTolerance(TimeSpan.Zero, TimeSpan.Zero, 5).ShouldBeTrue();
    }

    [Fact]
    public void WithinTolerance_ZeroSourceDuration_ReturnsFalseForNonZeroOutput()
    {
        DurationParity.WithinTolerance(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            5).ShouldBeFalse();
    }

    [Fact]
    public void WithinTolerance_NegativeOutputDifference_UsesAbsoluteValue()
    {
        DurationParity.WithinTolerance(
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(116),
            5).ShouldBeTrue();
    }
}
