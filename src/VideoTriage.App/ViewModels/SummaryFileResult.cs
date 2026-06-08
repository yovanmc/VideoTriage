namespace VideoTriage.App.ViewModels;

public sealed record SummaryFileResult(
    string FilePath,
    string Outcome,
    string Message,
    string? SavedPercent,
    string? FinalPath);
