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
            _host.Services.GetRequiredService<MainWindow>().Show();
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
