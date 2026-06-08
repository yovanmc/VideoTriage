using Shouldly;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Tests.Models;

public sealed class VerificationResultTests
{
    [Fact]
    public void IsValid_ReturnsTrueOnlyForValidOutcome()
    {
        var valid = new VerificationResult
        {
            Outcome = VerificationOutcome.Valid,
            Reason = "ok"
        };
        var failed = new VerificationResult
        {
            Outcome = VerificationOutcome.DecodeError,
            Reason = "corrupt"
        };

        valid.IsValid.ShouldBeTrue();
        failed.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(VerificationOutcome.MissingOrEmpty)]
    [InlineData(VerificationOutcome.ProbeFailed)]
    [InlineData(VerificationOutcome.DurationMismatch)]
    [InlineData(VerificationOutcome.ResolutionMismatch)]
    [InlineData(VerificationOutcome.AudioMissing)]
    [InlineData(VerificationOutcome.DecodeError)]
    public void IsValid_ReturnsFalse_ForEveryFailureOutcome(VerificationOutcome outcome)
    {
        var result = new VerificationResult { Outcome = outcome, Reason = "fail" };
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void OutputStats_IsNullByDefault()
    {
        var result = new VerificationResult
        {
            Outcome = VerificationOutcome.Valid,
            Reason = "ok"
        };
        result.OutputStats.ShouldBeNull();
    }
}
