using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

/// <summary>Single source of truth for how each <see cref="TriageOutcome"/> is shown.</summary>
public static class TriageOutcomeDisplay
{
    /// <summary>True when the file entered the encode pipeline (shown in the run summary).</summary>
    public static bool IsProcessed(TriageOutcome outcome) => outcome switch
    {
        TriageOutcome.Replaced or TriageOutcome.ReplacePartial
            or TriageOutcome.GrewKeptOriginal
            or TriageOutcome.OutputInvalid or TriageOutcome.EncodeFailed or TriageOutcome.ReplaceFailed
            or TriageOutcome.InsufficientSpace
            or TriageOutcome.Cancelled => true,
        _ => false,
    };

    public static string Label(TriageOutcome? outcome) => outcome switch
    {
        TriageOutcome.Replaced => "Re-encoded & replaced",
        TriageOutcome.ReplacePartial => "Replaced (recoverable partial)",
        TriageOutcome.GrewKeptOriginal => "Kept — encode was larger",
        TriageOutcome.OutputInvalid => "Verification failed — kept original",
        TriageOutcome.EncodeFailed => "Encode failed — kept original",
        TriageOutcome.ReplaceFailed => "Replace failed — kept original",
        TriageOutcome.InsufficientSpace => "Skipped — not enough free space",
        TriageOutcome.Cancelled => "Stopped",
        TriageOutcome.SkippedAlreadyAv1 => "Already AV1",
        TriageOutcome.SkippedLowBpp => "Below threshold",
        TriageOutcome.InvalidMetadata => "Couldn't read metadata",
        TriageOutcome.DryRunCandidate => "Would re-encode (dry run)",
        TriageOutcome.AlreadyCompleted => "Already processed",
        _ => "Done",
    };

    /// <summary>Coarse group used for the donut legend and status-bar severity.</summary>
    public static string GroupKey(TriageOutcome outcome) => outcome switch
    {
        TriageOutcome.Replaced or TriageOutcome.ReplacePartial => "Replaced",
        TriageOutcome.GrewKeptOriginal => "Kept larger",
        TriageOutcome.OutputInvalid or TriageOutcome.EncodeFailed or TriageOutcome.ReplaceFailed => "Failed",
        TriageOutcome.InsufficientSpace => "Low space",
        TriageOutcome.Cancelled => "Stopped",
        _ => "Other",
    };

    public static string GroupColor(TriageOutcome outcome) => GroupKey(outcome) switch
    {
        "Replaced" => "#36C98F",
        "Kept larger" => "#F5A524",
        "Failed" => "#F05252",
        "Low space" => "#5B8DEF",
        "Stopped" => "#8B93A7",
        _ => "#8B93A7",
    };

    /// <summary>True when an outcome should turn the status bar amber rather than green.</summary>
    public static bool IsWarning(TriageOutcome outcome) =>
        IsProcessed(outcome) && outcome is not (TriageOutcome.Replaced or TriageOutcome.ReplacePartial);
}
