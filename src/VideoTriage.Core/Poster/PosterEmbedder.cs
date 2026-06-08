using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Poster;

public sealed class PosterEmbedder(
    string ffmpegPath,
    IProcessRunner runner,
    IOutputVerifier verifier) : IPosterEmbedder
{
    public async Task<PosterEmbedResult> EmbedAsync(
        string verifiedEncodePath,
        VideoStats source,
        TriageOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.EmbedPoster)
            return Original(verifiedEncodePath, "Poster embedding disabled.");

        var posterPath = TempFileNaming.PosterImagePath(
            verifiedEncodePath,
            Environment.ProcessId);
        var muxedPath = TempFileNaming.PosterMuxPath(
            verifiedEncodePath,
            Environment.ProcessId);
        var keepMuxed = false;
        try
        {
            var timestamp = TimeSpan.FromSeconds(
                source.Duration.TotalSeconds * options.PosterTimestampPercent / 100);
            var grab = await RunAsync(
                PosterArguments.BuildFrameGrab(verifiedEncodePath, posterPath, timestamp),
                cancellationToken);
            if (!grab.Succeeded)
                return Original(verifiedEncodePath, "Poster frame extraction failed.");

            var mux = await RunAsync(
                PosterArguments.BuildCoverMux(verifiedEncodePath, posterPath, muxedPath),
                cancellationToken);
            if (!mux.Succeeded)
                return Original(verifiedEncodePath, "Poster mux failed.");

            var verified = await verifier.VerifyAsync(
                source,
                muxedPath,
                options,
                cancellationToken);
            if (!verified.IsValid)
            {
                return Original(
                    verifiedEncodePath,
                    $"Poster re-verification failed: {verified.Reason}");
            }

            keepMuxed = true;
            return new PosterEmbedResult
            {
                OutputPath = muxedPath,
                Embedded = true,
                Reason = "Poster embedded."
            };
        }
        finally
        {
            DeleteIfExists(posterPath);
            if (!keepMuxed)
                DeleteIfExists(muxedPath);
        }
    }

    private Task<ProcessResult> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken) =>
        runner.RunAsync(
            new ProcessRequest
            {
                FileName = ffmpegPath,
                Arguments = args,
                Timeout = TimeSpan.FromMinutes(5)
            },
            cancellationToken);

    private static PosterEmbedResult Original(string path, string reason) =>
        new() { OutputPath = path, Embedded = false, Reason = reason };

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
