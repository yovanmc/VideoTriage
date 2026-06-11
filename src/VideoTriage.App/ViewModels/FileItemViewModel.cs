using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    private string _metaLine = "";
    private string _statusText = "Queued";
    private double _progress;
    private bool _isProgressIndeterminate;
    private string _oldSizeText = "";
    private string _savedText = "";
    private string? _finalPath;
    private ImageSource? _thumbnail;

    public FileItemViewModel(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        FileName = Path.GetFileName(filePath);
    }

    public string FilePath { get; }
    public string FileName { get; }

    public string MetaLine
    {
        get => _metaLine;
        private set => SetProperty(ref _metaLine, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public string OldSizeText
    {
        get => _oldSizeText;
        private set => SetProperty(ref _oldSizeText, value);
    }

    public string SavedText
    {
        get => _savedText;
        private set => SetProperty(ref _savedText, value);
    }

    public string? FinalPath
    {
        get => _finalPath;
        private set => SetProperty(ref _finalPath, value);
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        internal set => SetProperty(ref _thumbnail, value);
    }

    public void ApplyProbe(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(result.FilePath),
                FilePath))
        {
            throw new ArgumentException("Probe result belongs to another file.", nameof(result));
        }

        if (!result.Succeeded || result.Stats is null)
        {
            MetaLine = "";
            StatusText = $"Probe failed: {result.Failure?.Message ?? "unknown error"}";
            return;
        }

        var stats = result.Stats;
        MetaLine =
            $"{stats.Width}x{stats.Height} | " +
            $"{stats.FramesPerSecond.ToString("0.##", CultureInfo.InvariantCulture)} fps | " +
            $"{HumanSize.Format(stats.FileSizeBytes)} | bpp " +
            $"{stats.BitsPerPixel.ToString("0.###", CultureInfo.InvariantCulture)}";
        StatusText = result.Classification?.Outcome switch
        {
            ClassificationOutcome.Candidate => "Candidate",
            ClassificationOutcome.SkipAlreadyAv1 => "Already AV1",
            ClassificationOutcome.SkipLowBpp => "Below threshold",
            _ => "Invalid metadata"
        };
    }

    public void Apply(FileProgress progressEvent)
    {
        ArgumentNullException.ThrowIfNull(progressEvent);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(progressEvent.FilePath),
                FilePath))
        {
            throw new ArgumentException("Progress event belongs to another file.", nameof(progressEvent));
        }

        if (progressEvent.EncodeProgress.HasValue)
            Progress = Math.Round(progressEvent.EncodeProgress.Value * 100, 1);

        double? computedSavedPct = null;
        if (progressEvent.Phase == TriagePhase.Done
            && progressEvent.Outcome == TriageOutcome.Replaced
            && progressEvent.Source is not null
            && progressEvent.OutputBytes.HasValue)
        {
            computedSavedPct = progressEvent.SavedPercent
                ?? (progressEvent.Source.FileSizeBytes > 0
                    ? (1.0 - (double)progressEvent.OutputBytes.Value / progressEvent.Source.FileSizeBytes) * 100.0
                    : 0.0);
        }

        StatusText = progressEvent.Phase switch
        {
            TriagePhase.Encoding => $"Encoding {Progress.ToString("0.#", CultureInfo.InvariantCulture)}%",
            TriagePhase.Verifying => "Verifying output",
            TriagePhase.EmbeddingPoster => "Embedding poster",
            TriagePhase.Replacing => "Replacing original",
            TriagePhase.Done => DoneText(progressEvent, computedSavedPct),
            _ => progressEvent.Phase.ToString()
        };

        if (!string.IsNullOrWhiteSpace(progressEvent.FinalPath))
            FinalPath = progressEvent.FinalPath;

        if (computedSavedPct.HasValue)
        {
            OldSizeText = HumanSize.Format(progressEvent.Source!.FileSizeBytes);
            var pct = computedSavedPct.Value.ToString("0.#", CultureInfo.InvariantCulture);
            SavedText = $"{HumanSize.Format(progressEvent.OutputBytes!.Value)}, -{pct}%";
        }
        else if (progressEvent.Phase == TriagePhase.Done)
        {
            OldSizeText = "";
            SavedText = "";
        }

        if (progressEvent.Phase == TriagePhase.Done)
            Progress = 100;

        IsProgressIndeterminate = progressEvent.Phase == TriagePhase.Encoding
            && !progressEvent.EncodeProgress.HasValue;
    }

    private static string DoneText(FileProgress progressEvent, double? computedSavedPct = null)
    {
        if (progressEvent.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial)
        {
            var pct = (computedSavedPct ?? progressEvent.SavedPercent ?? 0)
                .ToString("0.#", CultureInfo.InvariantCulture);
            return $"{TriageOutcomeDisplay.Label(progressEvent.Outcome)} · saved {pct}%";
        }

        return progressEvent.Outcome is { } o
            ? TriageOutcomeDisplay.Label(o)
            : progressEvent.Message ?? "Done";
    }
}
