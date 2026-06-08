using System.Globalization;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public sealed class SummaryViewModel
{
    public SummaryViewModel(TriageSummary summary)
    {
        ScannedCount = summary.Scanned;
        CandidateCount = summary.Candidates;
        ReplacedCount = summary.Replaced;
        ProcessedCount = summary.Files.Count(x => x.Phase == TriagePhase.Done);
        KeptCount = Math.Max(0, summary.Scanned - summary.Replaced);
        BytesSaved = summary.BytesSaved;
        BytesSavedText = HumanSize.Format(summary.BytesSaved);

        var totalSourceBytes = summary.Files
            .Where(x => x.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial)
            .Sum(x => x.Source?.FileSizeBytes ?? 0);
        AverageReductionPercent = totalSourceBytes == 0
            ? 0
            : 100d * summary.BytesSaved / totalSourceBytes;
        AverageReductionText =
            AverageReductionPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%";

        Segments =
        [
            new SummarySegment("Replaced", summary.Replaced, "#36C98F"),
            new SummarySegment("Kept / grew", summary.Grew, "#F5A524"),
            new SummarySegment("Invalid", summary.Invalid, "#8B93A7"),
            new SummarySegment("Failed", summary.Failed, "#F05252"),
            new SummarySegment("Skipped", summary.Skipped, "#5B8DEF")
        ];

        Files = summary.Files
            .Where(x => x.Phase == TriagePhase.Done)
            .Select(x => new SummaryFileResult(
                x.FilePath,
                x.Outcome?.ToString() ?? "Unknown",
                x.Message ?? string.Empty,
                x.SavedPercent is null
                    ? null
                    : x.SavedPercent.Value.ToString("0.0", CultureInfo.CurrentCulture) + "%",
                x.FinalPath))
            .ToArray();
    }

    public int ScannedCount { get; }
    public int CandidateCount { get; }
    public int ReplacedCount { get; }
    public int ProcessedCount { get; }
    public int KeptCount { get; }
    public long BytesSaved { get; }
    public string BytesSavedText { get; }
    public double AverageReductionPercent { get; }
    public string AverageReductionText { get; }
    public IReadOnlyList<SummarySegment> Segments { get; }
    public IReadOnlyList<SummaryFileResult> Files { get; }
}
