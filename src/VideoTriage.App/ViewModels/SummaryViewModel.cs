using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Services;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public enum SummarySeverity { None, Success, Warning }

public sealed class SummaryViewModel
{
    private readonly IExplorerLauncher? _explorerLauncher;

    public SummaryViewModel(
        TriageSummary summary,
        IReadOnlyDictionary<string, ImageSource?>? thumbnails = null,
        IExplorerLauncher? explorerLauncher = null)
    {
        _explorerLauncher = explorerLauncher;
        RevealCommand = new RelayCommand<string>(Reveal);

        var processed = summary.Files
            .Where(f => f.Phase == TriagePhase.Done && f.Outcome is { } o && TriageOutcomeDisplay.IsProcessed(o))
            .ToArray();

        ProcessedCount = processed.Length;
        ReplacedCount = processed.Count(f => f.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial);
        KeptOriginalCount = processed.Count(f => f.Outcome is TriageOutcome.GrewKeptOriginal);
        BytesSaved = summary.BytesSaved;
        BytesSavedText = HumanSize.Format(summary.BytesSaved);

        var totalSourceBytes = processed
            .Where(f => f.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial)
            .Sum(f => f.Source?.FileSizeBytes ?? 0);
        var reductionPercent = totalSourceBytes == 0 ? 0 : 100d * summary.BytesSaved / totalSourceBytes;
        OverallReductionText = reductionPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%";

        var duration = summary.CompletedAtUtc - summary.StartedAtUtc;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        CompletedAtText = summary.CompletedAtUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        DurationText = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : duration.TotalMinutes >= 1
                ? $"{duration.Minutes}m {duration.Seconds}s"
                : $"{duration.Seconds}s";

        Segments = processed
            .GroupBy(f => TriageOutcomeDisplay.GroupKey(f.Outcome!.Value))
            .Select(g => new SummarySegment(g.Key, g.Count(), TriageOutcomeDisplay.GroupColor(g.First().Outcome!.Value)))
            .ToArray();

        Severity = ProcessedCount == 0
            ? SummarySeverity.None
            : processed.Any(f => TriageOutcomeDisplay.IsWarning(f.Outcome!.Value))
                ? SummarySeverity.Warning
                : SummarySeverity.Success;

        Files = processed.Select(f =>
        {
            var outcome = f.Outcome!.Value;
            var isReplaced = outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial;
            var oldBytes = f.Source?.FileSizeBytes;
            var reveal = !string.IsNullOrWhiteSpace(f.FinalPath) ? f.FinalPath! : f.FilePath;
            return new SummaryFileResult(
                FileName: System.IO.Path.GetFileName(f.FilePath),
                FullPath: f.FilePath,
                StatusLabel: TriageOutcomeDisplay.Label(outcome),
                StatusColor: TriageOutcomeDisplay.GroupColor(outcome),
                OldSizeText: oldBytes is { } ob ? HumanSize.Format(ob) : "",
                NewSizeText: isReplaced && f.OutputBytes is { } nb ? HumanSize.Format(nb) : "",
                SavedText: isReplaced && f.SavedPercent is { } sp
                    ? sp.ToString("0.0", CultureInfo.CurrentCulture) + "%"
                    : null,
                FinalPath: f.FinalPath,
                RevealTargetPath: reveal,
                Thumbnail: GetThumb(thumbnails, f.FilePath));
        }).ToArray();
    }

    private static ImageSource? GetThumb(IReadOnlyDictionary<string, ImageSource?>? thumbs, string path)
    {
        if (thumbs is null) return null;
        return thumbs.TryGetValue(System.IO.Path.GetFullPath(path), out var img) ? img : null;
    }

    private void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) _explorerLauncher?.Open(dir);
    }

    public IRelayCommand<string> RevealCommand { get; }
    public int ProcessedCount { get; }
    public int ReplacedCount { get; }
    public int KeptOriginalCount { get; }
    public long BytesSaved { get; }
    public string BytesSavedText { get; }
    public string OverallReductionText { get; }
    public string CompletedAtText { get; }
    public string DurationText { get; }
    public SummarySeverity Severity { get; }
    public IReadOnlyList<SummarySegment> Segments { get; }
    public IReadOnlyList<SummaryFileResult> Files { get; }
}
