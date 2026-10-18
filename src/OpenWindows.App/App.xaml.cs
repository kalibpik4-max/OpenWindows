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

        var screenshotDir = GetArgValue(e.Args, "--screenshot");
        if (!string.IsNullOrEmpty(screenshotDir))
        {
            ScreenshotRunner.Start(window, screenshotDir, GetArgValue(e.Args, "--screenshot-pages"));
        }
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Shutdown();
        base.OnExit(e);
    }
}
