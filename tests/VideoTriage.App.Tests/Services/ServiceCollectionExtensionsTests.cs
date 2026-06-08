using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VideoTriage.App.Services;
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

    private sealed class FakeLocator(bool allAvailable) : IToolLocator
    {
        public string? FindOnPath(string executableName) =>
            allAvailable || executableName != "ffmpeg" ? $@"C:\tools\{executableName}.exe" : null;

        public ToolLocation RequireOnPath(string executableName) =>
            new() { Name = executableName, FullPath = FindOnPath(executableName)! };
    }
}
