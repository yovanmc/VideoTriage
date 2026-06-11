using Shouldly;

namespace VideoTriage.App.Tests.Views;

public sealed class MainWindowMarkupTests
{
    private static string ReadMainWindowXaml()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VideoTriage.App", "Views", "MainWindow.xaml"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void MainWindowMarkup_BindsFolderQueuePrerequisitesAndReservedToolbar()
    {
        var xaml = ReadMainWindowXaml();

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

    [Fact]
    public void Toolbar_BindsStartStopPauseResumeBackOpenData()
    {
        var xaml = ReadMainWindowXaml();
        xaml.ShouldContain("{Binding StartCommand}");
        xaml.ShouldContain("{Binding StopCommand}");
        xaml.ShouldContain("{Binding BackToQueueCommand}");
        xaml.ShouldContain("{Binding OpenDataDirectoryCommand}");
    }

    [Fact]
    public void Sidebar_HasNoDiagnosticsExpander()
    {
        ReadMainWindowXaml().ShouldNotContain("DiagnosticsView");
    }

    [Fact]
    public void StatusBar_BindsRunProgressAndQueueSummary()
    {
        var xaml = ReadMainWindowXaml();
        xaml.ShouldContain("RunProgressText");
        xaml.ShouldContain("QueueSummaryText");
    }

    [Fact]
    public void RecoveryBanner_BindsInterruptedNotice()
    {
        ReadMainWindowXaml().ShouldContain("InterruptedRunNotice");
    }
}
