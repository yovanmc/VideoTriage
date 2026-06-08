using Shouldly;
using VideoTriage.Core.Encoding;

namespace VideoTriage.Core.Tests.Encoding;

public sealed class HandBrakeProgressParserTests
{
    [Theory]
    [InlineData("""{"State":"WORKING","Working":{"Progress":0.43}}""", 0.43)]
    [InlineData("""{"Working":{"Progress":1.2}}""", 1.0)]
    [InlineData("""{"Working":{"Progress":-1}}""", 0.0)]
    public void TryParseProgress_ValidJson_ReturnsClampedValue(
        string line,
        double expected) =>
        HandBrakeProgressParser.TryParseProgress(line).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("Encoding: task 1")]
    [InlineData("""{"State":"WORKING"}""")]
    public void TryParseProgress_NonProgressLine_ReturnsNull(string line) =>
        HandBrakeProgressParser.TryParseProgress(line).ShouldBeNull();
}
