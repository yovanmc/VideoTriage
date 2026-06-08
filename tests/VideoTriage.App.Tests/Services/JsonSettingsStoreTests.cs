using Shouldly;
using VideoTriage.App.Models;
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Services;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "VideoTriage.SettingsTests",
        Guid.NewGuid().ToString("N"));

    public JsonSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults() =>
        new JsonSettingsStore(Path.Combine(_dir, "settings.json")).Load().ShouldBe(new AppSettings());

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);
        var expected = new AppSettings { CandidateBppThreshold = 0.21, DryRun = true };

        store.Save(expected);

        store.Load().ShouldBe(expected);
        File.Exists(path + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Load_MalformedJson_BacksUpInvalidFileAndReturnsDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{broken");
        var store = new JsonSettingsStore(path);

        store.Load().ShouldBe(new AppSettings());

        Directory.GetFiles(_dir, "settings.invalid.*.json").Length.ShouldBe(1);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void Load_UnsupportedSchemaVersion_ReturnsDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"schemaVersion":99}""");

        new JsonSettingsStore(path).Load().ShouldBe(new AppSettings());
    }
}
