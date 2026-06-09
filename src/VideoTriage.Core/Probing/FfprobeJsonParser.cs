using System.Globalization;
using System.IO;
using System.Text.Json;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public sealed class FfprobeJsonParser
{
    public VideoStats Parse(string filePath, long fileSizeBytes, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var streams = root.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.FirstOrDefault(IsVideoStream);

            if (video.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException("ffprobe JSON does not contain a video stream.");
            }

            return new VideoStats
            {
                FilePath = filePath,
                CodecName = RequiredString(video, "codec_name"),
                Width = RequiredPositiveInt(video, "width"),
                Height = RequiredPositiveInt(video, "height"),
                FramesPerSecond = RequiredFrameRate(video),
                Duration = RequiredDuration(video, root),
                FileSizeBytes = fileSizeBytes,
                VideoBitrateBitsPerSecond = OptionalLong(video, "bit_rate"),
                ContainerBitrateBitsPerSecond = TryGetFormat(root, out var format)
                    ? OptionalLong(format, "bit_rate")
                    : null,
                HasAudio = streams.Any(IsAudioStream),
                AttachedPicStreamIndex = FindAttachedPicStreamIndex(streams)
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("ffprobe JSON is invalid.", exception);
        }
    }

    private static bool HasVideoCodecType(JsonElement stream) =>
        stream.TryGetProperty("codec_type", out var codecType)
        && string.Equals(codecType.GetString(), "video", StringComparison.OrdinalIgnoreCase);

    private static bool HasAttachedPicDisposition(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition)
        && disposition.TryGetProperty("attached_pic", out var flag)
        && flag.ValueKind == JsonValueKind.Number
        && flag.GetInt32() == 1;

    private static bool IsVideoStream(JsonElement stream) =>
        HasVideoCodecType(stream) && !HasAttachedPicDisposition(stream);

    private static bool IsAudioStream(JsonElement stream) =>
        stream.TryGetProperty("codec_type", out var codecType)
        && string.Equals(codecType.GetString(), "audio", StringComparison.OrdinalIgnoreCase);

    private static bool IsAttachedPicStream(JsonElement stream) =>
        HasVideoCodecType(stream) && HasAttachedPicDisposition(stream);

    private static int? FindAttachedPicStreamIndex(JsonElement[] streams)
    {
        foreach (var stream in streams)
        {
            if (IsAttachedPicStream(stream)
                && stream.TryGetProperty("index", out var idx)
                && idx.TryGetInt32(out var value))
                return value;
        }
        return null;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"ffprobe JSON is missing required property '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static int RequiredPositiveInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value) || value <= 0)
        {
            throw new InvalidDataException($"ffprobe JSON has invalid positive integer property '{propertyName}'.");
        }

        return value;
    }

    private static double RequiredFrameRate(JsonElement video)
    {
        var raw = OptionalString(video, "avg_frame_rate");
        if (string.IsNullOrWhiteSpace(raw) || raw == "0/0")
        {
            raw = OptionalString(video, "r_frame_rate");
        }

        var frameRate = ParseRational(raw);
        if (frameRate <= 0)
        {
            throw new InvalidDataException("ffprobe JSON has invalid frame rate.");
        }

        return frameRate;
    }

    private static TimeSpan RequiredDuration(JsonElement video, JsonElement root)
    {
        var seconds = OptionalDouble(video, "duration");
        if (seconds is null && TryGetFormat(root, out var format))
        {
            seconds = OptionalDouble(format, "duration");
        }

        if (seconds is null or <= 0)
        {
            throw new InvalidDataException("ffprobe JSON has invalid duration.");
        }

        return TimeSpan.FromSeconds(seconds.Value);
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;

    private static long? OptionalLong(JsonElement element, string propertyName)
    {
        var raw = OptionalString(element, propertyName);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? OptionalDouble(JsonElement element, string propertyName)
    {
        var raw = OptionalString(element, propertyName);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double ParseRational(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var parts = raw.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator != 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static bool TryGetFormat(JsonElement root, out JsonElement format) =>
        root.TryGetProperty("format", out format) && format.ValueKind == JsonValueKind.Object;
}
