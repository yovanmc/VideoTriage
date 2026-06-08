using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoTriage.App.Views;
using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoTriage(this IServiceCollection services) =>
        services.AddVideoTriageCore();

    public static IServiceCollection AddVideoTriageForTests(this IServiceCollection services) =>
        services.AddVideoTriageCore();

    private static IServiceCollection AddVideoTriageCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IToolLocator, ToolLocator>();
        services.AddSingleton<IPrerequisiteService, PrerequisiteService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IVideoFileDiscovery, VideoFileDiscovery>();
        services.AddSingleton<IVideoClassifier, BppClassifier>();
        services.AddSingleton<FfprobeJsonParser>();
        services.AddSingleton<IFileRemover, FileRemover>();
        services.AddSingleton<ISafeReplacer, SafeReplacer>();
        services.AddSingleton<Func<string, ICompletedFileStore>>(
            _ => dir => new JsonLinesCompletedFileStore(Path.Combine(dir, "completed.jsonl")));
        services.AddSingleton<Func<string, IDeleteManifest>>(
            _ => dir => new CsvDeleteManifest(Path.Combine(dir, "deletions.csv")));
        services.AddSingleton<Func<string, IResultLog>>(
            _ => dir => new JsonLinesResultLog(Path.Combine(dir, "results.jsonl")));
        services.AddSingleton<ITriagePipelineProvider>(sp =>
        {
            var statuses = sp.GetRequiredService<IPrerequisiteService>().Check();
            if (statuses.Any(x => !x.IsAvailable))
                return new TriagePipelineProvider(null);

            var paths = statuses.ToDictionary(x => x.Name, x => x.FullPath!);
            var runner = sp.GetRequiredService<IProcessRunner>();
            var ffprobe = new FfprobeService(
                paths["ffprobe"],
                runner,
                sp.GetRequiredService<FfprobeJsonParser>());
            var verifier = new OutputVerifier(paths["ffmpeg"], runner, ffprobe);
            var encoder = new HandBrakeEncoder(
                paths["HandBrakeCLI"],
                runner,
                Path.Combine(AppContext.BaseDirectory, "Encoding", "Assets", "videotriage-av1.json"),
                "VideoTriage AV1");
            ITriagePipeline pipeline = new TriagePipeline(
                sp.GetRequiredService<IVideoFileDiscovery>(),
                ffprobe,
                sp.GetRequiredService<IVideoClassifier>(),
                encoder,
                verifier,
                sp.GetRequiredService<ISafeReplacer>(),
                sp.GetRequiredService<IFileSystem>(),
                sp.GetRequiredService<Func<string, ICompletedFileStore>>(),
                sp.GetRequiredService<Func<string, IDeleteManifest>>(),
                sp.GetRequiredService<Func<string, IResultLog>>());
            return new TriagePipelineProvider(pipeline);
        });
        services.AddSingleton<MainWindow>();

        return services;
    }
}

public interface ITriagePipelineProvider
{
    ITriagePipeline? Pipeline { get; }
}

public sealed class TriagePipelineProvider(ITriagePipeline? pipeline) : ITriagePipelineProvider
{
    public ITriagePipeline? Pipeline { get; } = pipeline;
}
