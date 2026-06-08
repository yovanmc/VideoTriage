using Shouldly;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Tests.Models;

public sealed class TriageOptionsM3Tests
{
    [Fact]
    public void DeepVerify_DefaultsToTrue()
    {
        new TriageOptions().DeepVerify.ShouldBeTrue();
    }

    [Fact]
    public void DurationTolerancePercent_DefaultsToFive()
    {
        new TriageOptions().DurationTolerancePercent.ShouldBe(5);
    }

    [Fact]
    public void RequireResolutionMatch_DefaultsToTrue()
    {
        new TriageOptions().RequireResolutionMatch.ShouldBeTrue();
    }

    [Fact]
    public void ResolutionTolerancePercent_DefaultsToTwo()
    {
        new TriageOptions().ResolutionTolerancePercent.ShouldBe(2);
    }

    [Fact]
    public void RequireAudioParity_DefaultsToTrue()
    {
        new TriageOptions().RequireAudioParity.ShouldBeTrue();
    }

    [Fact]
    public void AllFieldsCanBeOverriddenViaWith()
    {
        var options = new TriageOptions() with
        {
            DeepVerify = false,
            DurationTolerancePercent = 10,
            RequireResolutionMatch = false,
            ResolutionTolerancePercent = 5,
            RequireAudioParity = false
        };

        options.DeepVerify.ShouldBeFalse();
        options.DurationTolerancePercent.ShouldBe(10);
        options.RequireResolutionMatch.ShouldBeFalse();
        options.ResolutionTolerancePercent.ShouldBe(5);
        options.RequireAudioParity.ShouldBeFalse();
    }
}
