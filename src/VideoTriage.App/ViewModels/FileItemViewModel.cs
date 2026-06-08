using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    private string _metaLine = "";
    private string _statusText = "Queued";
    private double _progress;
    private string _savedText = "";
    private string? _finalPath;

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

        StatusText = progressEvent.Phase switch
        {
            TriagePhase.Encoding => $"Encoding {Progress.ToString("0.#", CultureInfo.InvariantCulture)}%",
            TriagePhase.Verifying => "Verifying output",
            TriagePhase.EmbeddingPoster => "Embedding poster",
            TriagePhase.Replacing => "Replacing original",
            TriagePhase.Done => DoneText(progressEvent),
            _ => progressEvent.Phase.ToString()
        };

        if (!string.IsNullOrWhiteSpace(progressEvent.FinalPath))
            SavedText = progressEvent.FinalPath;
    }

    private static string DoneText(FileProgress progressEvent) =>
        progressEvent.Outcome switch
        {
            TriageOutcome.Replaced =>
                $"Saved {(progressEvent.SavedPercent ?? 0).ToString("0.#", CultureInfo.InvariantCulture)}%",
            TriageOutcome.ReplacePartial => "Saved as recoverable partial",
            TriageOutcome.OutputInvalid => "Verification failed; original kept",
            TriageOutcome.GrewKeptOriginal => "Encode grew; original kept",
            TriageOutcome.Cancelled => "Cancelled; original kept",
            _ => progressEvent.Message ?? "Done"
        };
}
