using System.Windows.Media;

namespace VideoTriage.App.ViewModels;

public sealed record SummaryFileResult(
    string FileName,
    string FullPath,
    string StatusLabel,
    string StatusColor,
    string OldSizeText,
    string NewSizeText,
    string? SavedText,
    string? FinalPath,
    string RevealTargetPath,
    ImageSource? Thumbnail);
