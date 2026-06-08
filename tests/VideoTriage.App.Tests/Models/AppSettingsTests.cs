using Shouldly;
using VideoTriage.App.Models;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.Models;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_AreSafeForFirstRun()
    {
        var settings = new AppSettings();

        settings.CandidateBppThreshold.ShouldBe(0.13);
        settings.DeleteMode.ShouldBe(DeleteMode.RecycleBin);
        settings.DeepVerify.ShouldBeTrue();
        settings.EmbedPoster.ShouldBeTrue();
        settings.MinimumFreeGigabytes.ShouldBe(5);
        settings.DryRun.ShouldBeFalse();
    }

    [Fact]
    public void ToTriageOptions_MapsEveryEditableField()
    {
        var settings = new AppSettings
        {
            CandidateBppThreshold = 0.2,
            DeleteMode = DeleteMode.Permanent,
            DeepVerify = false,
            EmbedPoster = false,
            MinimumFreeGigabytes = 9,
            DryRun = true
        };

        var options = settings.ToTriageOptions();

        options.CandidateBppThreshold.ShouldBe(0.2);
        options.DeleteMode.ShouldBe(DeleteMode.Permanent);
        options.DeepVerify.ShouldBeFalse();
        options.EmbedPoster.ShouldBeFalse();
        options.MinimumFreeGigabytes.ShouldBe(9);
        options.DryRun.ShouldBeTrue();
    }
}
