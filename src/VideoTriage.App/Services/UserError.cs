namespace VideoTriage.App.Services;

public sealed record UserError(
    DateTimeOffset Timestamp,
    UserErrorSeverity Severity,
    string Title,
    string Message,
    string? Detail);
