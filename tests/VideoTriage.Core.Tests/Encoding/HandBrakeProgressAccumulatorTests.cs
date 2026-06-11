using Shouldly;
using VideoTriage.Core.Encoding;

namespace VideoTriage.Core.Tests.Encoding;

public sealed class HandBrakeProgressAccumulatorTests
{
    private static readonly string[] WorkingObject =
    [
        "Progress: {",
        "    \"State\": \"WORKING\",",
        "    \"Working\": {",
        "        \"Progress\": 0.42,",
        "        \"ETASeconds\": 87,",
        "    }",
        "}",
    ];

    [Fact]
    public void Accumulate_MultiLineWorkingObject_EmitsProgressAndEta()
    {
        var acc = new HandBrakeProgressAccumulator();
        HandBrakeProgress? emitted = null;
        foreach (var line in WorkingObject)
        {
            var r = acc.Append(line);
            if (r is not null) emitted = r;
        }

        emitted.ShouldNotBeNull();
        emitted!.Progress.ShouldBe(0.42, 0.0001);
        emitted.EtaSeconds.ShouldBe(87);
    }

    [Fact]
    public void Accumulate_NonWorkingObjects_EmitNothing()
    {
        var acc = new HandBrakeProgressAccumulator();
        string[] noise =
        [
            "Version: {", "    \"Version\": {", "    },", "}",
            "Progress: {", "    \"Muxing\": { \"Progress\": 0.0 },", "    \"State\": \"MUXING\"", "}",
            "Progress: {", "    \"State\": \"WORKDONE\",", "    \"WorkDone\": {", "    }", "}",
        ];
        var got = noise.Select(acc.Append).Where(x => x is not null).ToList();
        got.ShouldBeEmpty();
    }

    [Fact]
    public void Accumulate_ProgressClampedToUnitInterval()
    {
        var acc = new HandBrakeProgressAccumulator();
        HandBrakeProgress? emitted = null;
        foreach (var line in new[] { "Progress: {", "\"Working\": { \"Progress\": 1.5 }", "}" })
            emitted = acc.Append(line) ?? emitted;
        emitted!.Progress.ShouldBe(1.0);
    }

    [Fact]
    public void Append_CalledConcurrently_DoesNotThrowOrCorrupt()
    {
        var acc = new HandBrakeProgressAccumulator();
        // Two complete one-line objects; feeding many in parallel must never throw
        // (e.g. StringBuilder index corruption) and any emitted value must be valid.
        string[] objects =
        [
            "Progress: { \"Working\": { \"Progress\": 0.5 } }",
            "Progress: { \"Working\": { \"Progress\": 0.75 } }",
        ];

        Parallel.For(0, 2000, i =>
        {
            var r = acc.Append(objects[i % objects.Length]);
            if (r is not null)
            {
                r.Progress.ShouldBeInRange(0.0, 1.0);
            }
        });
    }
}
