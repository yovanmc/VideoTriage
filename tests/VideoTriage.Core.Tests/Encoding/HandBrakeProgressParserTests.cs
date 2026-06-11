using Shouldly;
using VideoTriage.Core.Encoding;

namespace VideoTriage.Core.Tests.Encoding;

public sealed class HandBrakeProgressParserTests
{
    [Fact]
    public void TryParse_WorkingProgress_ReturnsValue()
    {
        var json = "{ \"State\": \"WORKING\", \"Working\": { \"Progress\": 0.5 } }";
        HandBrakeProgressParser.TryParse(json)!.Progress.ShouldBe(0.5);
    }

    [Fact]
    public void TryParse_TrailingCommaTolerated()
    {
        var json = "{ \"Working\": { \"Progress\": 0.25, } }";
        HandBrakeProgressParser.TryParse(json)!.Progress.ShouldBe(0.25);
    }

    [Fact]
    public void TryParse_NonWorking_ReturnsNull()
    {
        HandBrakeProgressParser.TryParse("{ \"State\": \"MUXING\" }").ShouldBeNull();
    }

    [Theory]
    [InlineData("{ \"Working\": { \"Progress\": 1.2 } }", 1.0)]
    [InlineData("{ \"Working\": { \"Progress\": -1 } }", 0.0)]
    public void TryParse_OutOfRangeProgress_IsClamped(string json, double expected) =>
        HandBrakeProgressParser.TryParse(json)!.Progress.ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("Encoding: task 1")]
    [InlineData("{ \"State\": \"WORKING\" }")]
    public void TryParse_NonProgressInput_ReturnsNull(string json) =>
        HandBrakeProgressParser.TryParse(json).ShouldBeNull();

    [Fact]
    public void TryParse_WorkingWithEta_ReturnsEta()
    {
        var json = "{ \"Working\": { \"Progress\": 0.5, \"ETASeconds\": 120 } }";
        HandBrakeProgressParser.TryParse(json)!.EtaSeconds.ShouldBe(120);
    }
}
