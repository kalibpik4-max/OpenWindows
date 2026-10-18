using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace OpenWindows.Services;

public static class ThemeService
{
    private static Window? watchedWindow;

    public static void Apply(string theme, Window? window)
    {
        try
        {
            if (window != null && !ReferenceEquals(window, watchedWindow) && watchedWindow != null)
            {
                SystemThemeWatcher.UnWatch(watchedWindow);
                watchedWindow = null;
            }

            if (string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase))
            {
                ApplicationThemeManager.ApplySystemTheme(true);
                if (window != null)
                {
                    SystemThemeWatcher.Watch(window);
                    watchedWindow = window;
                }
                return;
            }

            if (watchedWindow != null)
            {
                SystemThemeWatcher.UnWatch(watchedWindow);
                watchedWindow = null;
            }

            var requested = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
            ApplicationThemeManager.Apply(requested, WindowBackdropType.Mica, true);
        }
        catch (Exception ex)
        {
            Log.Write("Theme apply failed: " + ex.Message);
        }
    }
}
