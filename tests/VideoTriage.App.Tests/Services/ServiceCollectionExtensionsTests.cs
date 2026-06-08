using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.App.Tests.Fakes;
using VideoTriage.App.ViewModels;
using VideoTriage.App.Views;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Tools;

namespace VideoTriage.App.Tests.Services;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVideoTriage_RegistersDefaultToolLocator()
    {
        var services = new ServiceCollection();

        services.AddVideoTriageForTests();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolLocator>().ShouldBeOfType<ToolLocator>();
    }

    [Fact]
    public void AddVideoTriage_AllToolsAvailable_RegistersRealPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolLocator>(new FakeLocator(allAvailable: true));

        services.AddVideoTriageForTests();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPrerequisiteService>().ShouldBeOfType<PrerequisiteService>();
        provider.GetRequiredService<ITriagePipelineProvider>().Pipeline
            .ShouldBeOfType<TriagePipeline>();
    }

    [Fact]
    public void AddVideoTriage_MissingTool_RegistersProviderWithoutPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolLocator>(new FakeLocator(allAvailable: false));

        services.AddVideoTriageForTests();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITriagePipelineProvider>().Pipeline.ShouldBeNull();
        provider.GetRequiredService<IPrerequisiteService>().Check()
            .Any(x => !x.IsAvailable).ShouldBeTrue();
    }

    [Fact]
    public void AddVideoTriage_FfprobeAvailable_RegistersScannableMainViewModel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolLocator>(new FakeLocator(allAvailable: true));
        services.AddSingleton<IDialogService>(new FakeDialogService());
        services.AddSingleton<IUiDispatcher>(new RecordingUiDispatcher());

        services.AddVideoTriageForTests();
        using var provider = services.BuildServiceProvider();

        var viewModel = provider.GetRequiredService<MainViewModel>();

        viewModel.ChooseFolderCommand.CanExecute(null).ShouldBeTrue();
        viewModel.SelectedFolder = @"C:\Videos";
        viewModel.StartCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void AddVideoTriage_FfprobeMissing_StillRegistersMainViewModel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolLocator>(new MissingFfprobeLocator());
        services.AddSingleton<IDialogService>(new FakeDialogService());
        services.AddSingleton<IUiDispatcher>(new RecordingUiDispatcher());

        services.AddVideoTriageForTests();
        using var provider = services.BuildServiceProvider();

        var viewModel = provider.GetRequiredService<MainViewModel>();

        viewModel.ChooseFolderCommand.CanExecute(null).ShouldBeFalse();
        viewModel.Prerequisites.Single(x => x.Name == "ffprobe").IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void AddVideoTriage_FfprobeMissing_StillResolvesMainWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<IToolLocator>(new MissingFfprobeLocator());
                services.AddSingleton<IDialogService>(new FakeDialogService());
                services.AddSingleton<IUiDispatcher>(new RecordingUiDispatcher());

                services.AddVideoTriageForTests();
                using var provider = services.BuildServiceProvider();
                var window = provider.GetRequiredService<MainWindow>();
                var viewModel = window.DataContext.ShouldBeOfType<MainViewModel>();

                viewModel.ChooseFolderCommand.CanExecute(null).ShouldBeFalse();
                viewModel.Prerequisites.Single(x => x.Name == "ffprobe").IsAvailable.ShouldBeFalse();
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();

        thread.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue("STA window resolution timed out.");
        failure.ShouldBeNull();
    }

    private sealed class FakeLocator(bool allAvailable) : IToolLocator
    {
        public string? FindOnPath(string executableName) =>
            allAvailable || executableName != "ffmpeg" ? $@"C:\tools\{executableName}.exe" : null;

        public ToolLocation RequireOnPath(string executableName) =>
            new() { Name = executableName, FullPath = FindOnPath(executableName)! };
    }

    private sealed class MissingFfprobeLocator : IToolLocator
    {
        public string? FindOnPath(string executableName) =>
            executableName == "ffprobe" ? null : $@"C:\tools\{executableName}.exe";

        public ToolLocation RequireOnPath(string executableName) =>
            new() { Name = executableName, FullPath = FindOnPath(executableName)! };
    }
}
