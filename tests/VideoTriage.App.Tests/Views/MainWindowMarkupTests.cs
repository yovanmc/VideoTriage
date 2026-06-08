using Shouldly;

namespace VideoTriage.App.Tests.Views;

public sealed class MainWindowMarkupTests
{
    [Fact]
    public void MainWindowMarkup_BindsFolderQueuePrerequisitesAndReservedToolbar()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VideoTriage.App", "Views", "MainWindow.xaml"));
        var xaml = File.ReadAllText(path);

        xaml.ShouldContain("Command=\"{Binding ChooseFolderCommand}\"");
        xaml.ShouldContain("ItemsSource=\"{Binding Items}\"");
        xaml.ShouldContain("ItemsSource=\"{Binding Prerequisites}\"");
        xaml.ShouldContain("x:Name=\"StartButton\"");
        xaml.ShouldContain("x:Name=\"PauseButton\"");
        xaml.ShouldContain("x:Name=\"ResumeButton\"");
        xaml.ShouldContain("x:Name=\"StopButton\"");
        xaml.ShouldContain("Command=\"{Binding StartCommand}\"");
        xaml.ShouldContain("Command=\"{Binding PauseCommand}\"");
        xaml.ShouldContain("Command=\"{Binding ResumeCommand}\"");
        xaml.ShouldContain("Command=\"{Binding StopCommand}\"");
        xaml.ShouldNotContain("sample.mp4");
    }
}
