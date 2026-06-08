namespace VideoTriage.Core.Models;

public enum VerificationOutcome
{
    Valid,
    MissingOrEmpty,
    ProbeFailed,
    DurationMismatch,
    ResolutionMismatch,
    AudioMissing,
    DecodeError
}
