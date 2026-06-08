namespace VideoTriage.Core.FileSystem;

/// <summary>
/// Centralizes the naming of all VideoTriage temporary artifacts so discovery can exclude them
/// and the replacement pipeline can reason about distinct, non-colliding paths.
/// </summary>
public static class TempFileNaming
{
    public const string EncodeInfix = ".videotriage.tmp.";
    public const string StagingInfix = ".videotriage.staging.";
    public const string PartialInfix = ".videotriage.partial.";
    public const string PosterInfix = ".videotriage.poster.";

    /// <summary>The encoder writes its candidate here.</summary>
    public static string EncodePath(string sourcePath, int processId) =>
        Build(sourcePath, EncodeInfix, processId, ".mp4");

    /// <summary>
    /// SafeReplacer stages the verified candidate here before removing the original. MUST be
    /// distinct from <see cref="EncodePath"/> so moving the encoder output into staging is never a
    /// same-path move (which throws).
    /// </summary>
    public static string StagingPath(string sourcePath, int processId) =>
        Build(sourcePath, StagingInfix, processId, ".mp4");

    /// <summary>Verified bytes preserved here if the final rename fails after the original is gone.</summary>
    public static string PartialPath(string sourcePath, int processId) =>
        Build(sourcePath, PartialInfix, processId, ".mp4");

    public static string PosterImagePath(string encodePath, int processId) =>
        Build(encodePath, PosterInfix, processId, ".jpg");

    public static string PosterMuxPath(string encodePath, int processId) =>
        Build(encodePath, PosterInfix, processId, ".mp4");

    public static bool IsTempArtifact(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains(EncodeInfix, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(StagingInfix, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(PartialInfix, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(PosterInfix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Build(string path, string infix, int processId, string extension) =>
        Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}{infix}{processId}{extension}");
}
