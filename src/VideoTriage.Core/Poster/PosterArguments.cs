using System.Globalization;

namespace VideoTriage.Core.Poster;

public static class PosterArguments
{
    public static IReadOnlyList<string> BuildFrameGrab(
        string encodePath,
        string posterPath,
        TimeSpan timestamp) =>
    [
        "-nostdin",
        "-ss",
        timestamp.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
        "-i",
        encodePath,
        "-frames:v",
        "1",
        "-vf",
        "thumbnail",
        "-y",
        posterPath
    ];

    public static IReadOnlyList<string> BuildCoverMux(
        string encodePath,
        string posterPath,
        string muxedPath) =>
    [
        "-nostdin",
        "-i",
        encodePath,
        "-i",
        posterPath,
        "-map",
        "0",
        "-map",
        "1",
        "-c",
        "copy",
        "-c:v:1",
        "mjpeg",
        "-disposition:v:1",
        "attached_pic",
        "-y",
        muxedPath
    ];
}
