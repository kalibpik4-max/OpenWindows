using System.Windows;
using OpenWindows.Services;

namespace OpenWindows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppServices.Initialize();

        var settings = AppServices.Settings.Current;
        ThemeService.Apply(settings.Theme, null);

        if (settings.ToastNotificationsEnabled)
        {
            AppServices.Notifications.Register();
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
        {
            window.Hide();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Shutdown();
        base.OnExit(e);
    }
}
