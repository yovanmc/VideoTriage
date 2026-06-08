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
}
