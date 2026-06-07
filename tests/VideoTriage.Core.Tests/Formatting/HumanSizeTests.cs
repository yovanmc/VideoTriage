using Shouldly;
using VideoTriage.Core.Formatting;
using Xunit;

namespace VideoTriage.Core.Tests.Formatting;

public class HumanSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1610612736, "1.5 GB")]
    public void Format_ReturnsHumanReadable(long bytes, string expected)
    {
        HumanSize.Format(bytes).ShouldBe(expected);
    }

    [Fact]
    public void Format_NegativeBytes_IsTreatedAsZero()
    {
        HumanSize.Format(-5).ShouldBe("0 B");
    }
}
