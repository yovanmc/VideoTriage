using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoTriage.App.Services;
using VideoTriage.App.Views;

namespace VideoTriage.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddVideoTriage())
                .Build();

            _host.StartAsync().GetAwaiter().GetResult();
            _ = _host.Services.GetRequiredService<ITriagePipelineProvider>();
            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Show();
            MaybeStartAutomation(window, e.Args);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "VideoTriage startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            try
            {
                _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch
            {
                // Preserve the original startup failure shown to the user.
            }
            _host?.Dispose();
            _host = null;
            Shutdown(1);
        }
    }

    // Headless automation hooks for the screenshot harness. `--folder` (read-only scan) is honored
    // in all builds; `--autostart` (runs the real pipeline) and `--done-signal` are DEBUG-only so
    // shipped builds can never auto-run a destructive encode from a command line.
    private static void MaybeStartAutomation(MainWindow window, string[] args)
    {
        var folder = GetArgValue(args, "--folder");
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (window.DataContext is not ViewModels.MainViewModel vm) return;

        var autoStart = false;
        string? doneSignal = null;
#if DEBUG
        autoStart = args.Contains("--autostart");
        doneSignal = GetArgValue(args, "--done-signal");
#endif
        _ = window.Dispatcher.InvokeAsync(() => vm.RunAutomationAsync(folder!, autoStart, doneSignal));
    }

    private static string? GetArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
