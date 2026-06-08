using Shouldly;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Verify;

public sealed class FfmpegStderrFilterTests
{
    [Fact]
    public void RealErrorLines_EmptyString_ReturnsEmpty()
    {
        FfmpegStderrFilter.RealErrorLines(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void RealErrorLines_OnlyWhitespaceAndBlankLines_ReturnsEmpty()
    {
        FfmpegStderrFilter.RealErrorLines("   \r\n\r\n  \n  ").ShouldBeEmpty();
    }

    [Fact]
    public void RealErrorLines_OnlyBenignDtsLines_ReturnsEmpty()
    {
        var stderr =
            "DTS 12345, next:12346 st:0 invalid dropping\r\n" +
            "non monotonically increasing dts to muxer in stream 0\r\n" +
            "non-monotonically increasing dts to muxer in stream 0";

        FfmpegStderrFilter.RealErrorLines(stderr).ShouldBeEmpty();
    }

    [Fact]
    public void RealErrorLines_OnlyRepeatMessageLines_ReturnsEmpty()
    {
        var stderr =
            "Last message repeated 3 times\r\n" +
            "Last message repeated 17 times";

        FfmpegStderrFilter.RealErrorLines(stderr).ShouldBeEmpty();
    }

    [Fact]
    public void RealErrorLines_BenignAndReal_ReturnsOnlyRealLines()
    {
        var stderr =
            "non monotonically increasing dts to muxer in stream 0\r\n" +
            "error while decoding MB 42 50, bytestream -7\r\n" +
            "Last message repeated 2 times\r\n" +
            "moov atom not found";

        var errors = FfmpegStderrFilter.RealErrorLines(stderr);

        errors.Count.ShouldBe(2);
        errors[0].ShouldBe("error while decoding MB 42 50, bytestream -7");
        errors[1].ShouldBe("moov atom not found");
    }

    [Fact]
    public void RealErrorLines_MultipleRealErrors_ReturnsAll()
    {
        var stderr =
            "Invalid data found when processing input\n" +
            "corrupt decoded frame in stream 0\n" +
            "concealing 137 DC, 137 AC, 137 MV errors in I frame";

        var errors = FfmpegStderrFilter.RealErrorLines(stderr);

        errors.Count.ShouldBe(3);
        errors[0].ShouldBe("Invalid data found when processing input");
        errors[1].ShouldBe("corrupt decoded frame in stream 0");
        errors[2].ShouldBe("concealing 137 DC, 137 AC, 137 MV errors in I frame");
    }

    [Fact]
    public void RealErrorLines_CaseInsensitiveMatchForBenignPatterns()
    {
        var stderr =
            "Non Monotonically Increasing DTS to muxer in stream 0\r\n" +
            "LAST MESSAGE REPEATED 5 TIMES";

        FfmpegStderrFilter.RealErrorLines(stderr).ShouldBeEmpty();
    }

    [Fact]
    public void FirstRealErrorLine_StopsAfterFirstRealError()
    {
        var lines = LinesThatThrowAfterFirstError();

        FfmpegStderrFilter.FirstRealErrorLine(lines)
            .ShouldBe("corrupt decoded frame");
    }

    private static IEnumerable<string> LinesThatThrowAfterFirstError()
    {
        yield return "non monotonically increasing dts to muxer in stream 0";
        yield return "corrupt decoded frame";
        throw new InvalidOperationException("Enumeration should have stopped.");
    }
}
