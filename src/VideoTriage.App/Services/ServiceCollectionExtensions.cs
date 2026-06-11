using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using VideoTriage.App.ViewModels;
using VideoTriage.App.Views;
using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Poster;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoTriage(this IServiceCollection services) =>
        services.AddVideoTriageCore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoTriage",
            "settings.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoTriage",
                "Logs"));

    public static IServiceCollection AddVideoTriageForTests(this IServiceCollection services) =>
        services.AddVideoTriageCore(Path.Combine(
            Path.GetTempPath(),
            "VideoTriage.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json"),
            Path.Combine(
                Path.GetTempPath(),
                "VideoTriage.Tests",
                Guid.NewGuid().ToString("N"),
                "Logs"));

    private static IServiceCollection AddVideoTriageCore(
        this IServiceCollection services,
        string settingsPath,
        string logDirectory)
    {
        services.TryAddSingleton<IToolLocator, ToolLocator>();
        services.AddSingleton<IPrerequisiteService, PrerequisiteService>();
        services.TryAddSingleton<IExplorerLauncher, ExplorerLauncher>();
        services.TryAddSingleton<IDialogService, DialogService>();
        services.TryAddSingleton<IUiDispatcher>(
            _ => new UiDispatcher(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher));
        services.TryAddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsPath));
        services.TryAddSingleton(new RollingFileLogPath(logDirectory));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, RollingFileLoggerProvider>());
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
        services.TryAddSingleton<IAppLog, AppLog>();
        services.TryAddSingleton<IUserErrorSink, UserErrorSink>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IThumbnailService>(sp =>
        {
            var statuses = sp.GetRequiredService<IPrerequisiteService>().Check();
            var ffmpegPath = statuses.SingleOrDefault(x => x.Name == "ffmpeg" && x.IsAvailable)?.FullPath;
            if (string.IsNullOrEmpty(ffmpegPath))
                return new NullThumbnailService();
            return new FfmpegThumbnailService(ffmpegPath, sp.GetRequiredService<IProcessRunner>());
        });
        services.AddSingleton<ApplicationWorkLifetime>();
        services.AddSingleton<IApplicationWorkLifetime>(sp => sp.GetRequiredService<ApplicationWorkLifetime>());
        services.AddSingleton<IRunLeaseFactory, FileRunLeaseFactory>();
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
        services.AddSingleton<Func<string, IReplacementTransactionCoordinator>>(sp =>
            dir => new ReplacementTransactionCoordinator(
                new JsonLinesReplacementJournal(Path.Combine(dir, "replacement-journal.jsonl")),
                sp.GetRequiredService<IFileSystem>(),
                sp.GetRequiredService<IFileRemover>(),
                new CsvDeleteManifest(Path.Combine(dir, "deletions.csv"))));
        services.AddSingleton<Func<string, IReplacementRecovery>>(sp =>
            dir => new ReplacementRecovery(
                new JsonLinesReplacementJournal(Path.Combine(dir, "replacement-journal.jsonl")),
                sp.GetRequiredService<IFileSystem>(),
                new CsvDeleteManifest(Path.Combine(dir, "deletions.csv"))));
        services.AddSingleton<Func<string, IActiveRunJournal>>(
            _ => dir => new JsonActiveRunJournal(dir));
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
            var posterEmbedder = new PosterEmbedder(paths["ffmpeg"], runner, verifier);
            var encoder = new HandBrakeEncoder(
                paths["HandBrakeCLI"],
                runner,
                Path.Combine(AppContext.BaseDirectory, "Encoding", "Assets", "videotriage-av1.json"),
                "VideoTriage AV1");
            ITriagePipeline pipeline = new TriagePipeline(
                sp.GetRequiredService<IRunLeaseFactory>(),
                ffprobe,
                sp.GetRequiredService<IVideoClassifier>(),
                encoder,
                verifier,
                sp.GetRequiredService<ISafeReplacer>(),
                sp.GetRequiredService<IFileSystem>(),
                sp.GetRequiredService<Func<string, ICompletedFileStore>>(),
                sp.GetRequiredService<Func<string, IDeleteManifest>>(),
                sp.GetRequiredService<Func<string, IResultLog>>(),
                posterEmbedder,
                sp.GetRequiredService<Func<string, IReplacementTransactionCoordinator>>(),
                sp.GetRequiredService<Func<string, IReplacementRecovery>>(),
                sp.GetRequiredService<Func<string, IActiveRunJournal>>());
            return new TriagePipelineProvider(pipeline);
        });
        services.AddSingleton(sp =>
        {
            var prerequisiteService = sp.GetRequiredService<IPrerequisiteService>();
            var statuses = prerequisiteService.Check();
            var ffprobePath = statuses.SingleOrDefault(x => x.Name == "ffprobe" && x.IsAvailable)?.FullPath;
            IFolderProbeScanner? scanner = null;

            if (!string.IsNullOrWhiteSpace(ffprobePath))
            {
                scanner = new FolderProbeScanner(
                    sp.GetRequiredService<IVideoFileDiscovery>(),
                    new FfprobeService(
                        ffprobePath,
                        sp.GetRequiredService<IProcessRunner>(),
                        sp.GetRequiredService<FfprobeJsonParser>()),
                    sp.GetRequiredService<IVideoClassifier>());
            }

            var settings = sp.GetRequiredService<SettingsViewModel>();
            return new MainViewModel(
                scanner,
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IUiDispatcher>(),
                prerequisiteService,
                sp.GetRequiredService<ITriagePipelineProvider>(),
                settings: settings,
                appLog: sp.GetRequiredService<IAppLog>(),
                userErrors: sp.GetRequiredService<IUserErrorSink>(),
                diagnostics: sp.GetRequiredService<DiagnosticsViewModel>(),
                activeRunJournalFactory: sp.GetRequiredService<Func<string, IActiveRunJournal>>(),
                thumbnailService: sp.GetRequiredService<IThumbnailService>(),
                workLifetime: sp.GetRequiredService<IApplicationWorkLifetime>(),
                explorerLauncher: sp.GetRequiredService<IExplorerLauncher>());
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

internal sealed class NullThumbnailService : IThumbnailService
{
    public Task<System.Windows.Media.Imaging.BitmapSource?> GetAsync(
        string filePath, int streamIndex, CancellationToken cancellationToken) =>
        Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);
}
