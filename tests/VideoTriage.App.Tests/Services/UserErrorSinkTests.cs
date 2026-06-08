using Shouldly;
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Services;

public sealed class UserErrorSinkTests
{
    [Fact]
    public void Add_RecordsAllFields()
    {
        var now = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);
        var sink = new UserErrorSink(() => now);

        sink.Add(UserErrorSeverity.Error, "Run failed", "The file was not changed.", "boom");

        sink.Errors.ShouldBe([
            new UserError(now, UserErrorSeverity.Error, "Run failed",
                "The file was not changed.", "boom")
        ]);
    }

    [Fact]
    public void Add_MoreThanTwoHundred_KeepsNewestTwoHundred()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);

        for (var index = 0; index < 205; index++)
            sink.Add(UserErrorSeverity.Warning, $"title-{index}", $"message-{index}");

        sink.Errors.Count.ShouldBe(200);
        sink.Errors[0].Title.ShouldBe("title-5");
        sink.Errors[^1].Title.ShouldBe("title-204");
    }

    [Fact]
    public void Errors_ReturnsSnapshot()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        sink.Add(UserErrorSeverity.Info, "First", "one");
        var snapshot = sink.Errors;

        sink.Add(UserErrorSeverity.Info, "Second", "two");

        snapshot.Count.ShouldBe(1);
        sink.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        sink.Add(UserErrorSeverity.Info, "Ready", "Ready.");

        sink.Clear();

        sink.Errors.ShouldBeEmpty();
    }
}
