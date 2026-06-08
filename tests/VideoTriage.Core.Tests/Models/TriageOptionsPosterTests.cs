using Shouldly;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Tests.Models;

public sealed class TriageOptionsPosterTests
{
    [Fact]
    public void Defaults_EnablePosterAtTenPercent()
    {
        var options = new TriageOptions();

        options.EmbedPoster.ShouldBeTrue();
        options.PosterTimestampPercent.ShouldBe(10);
    }
}
