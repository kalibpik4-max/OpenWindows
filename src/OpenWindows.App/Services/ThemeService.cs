using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace OpenWindows.Services;

public static class ThemeService
{
    private static Window? watchedWindow;
    private static bool hooked;

    public static void Apply(string theme, Window? window)
    {
        try
        {
            if (!hooked)
            {
                hooked = true;
                ApplicationThemeManager.Changed += (applied, _) => SwapPalette(applied);
            }

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
                SwapPalette(ApplicationThemeManager.GetAppTheme());
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
            SwapPalette(requested);
        }
        catch (Exception ex)
        {
            Log.Write("Theme apply failed: " + ex.Message);
        }
    }

    private static void SwapPalette(ApplicationTheme theme)
    {
        try
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            var source = theme == ApplicationTheme.Light
                ? "Themes/PaletteLight.xaml"
                : "Themes/PaletteDark.xaml";
            var dictionaries = app.Resources.MergedDictionaries;
            var replacement = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

            for (var i = 0; i < dictionaries.Count; i++)
            {
                var candidate = dictionaries[i];
                if (candidate.Source != null &&
                    candidate.Source.OriginalString.Contains("Themes/Palette", StringComparison.OrdinalIgnoreCase))
                {
                    dictionaries[i] = replacement;
                    return;
                }
            }

            dictionaries.Add(replacement);
        }
        catch (Exception ex)
        {
            Log.Write("Palette swap failed: " + ex.Message);
        }
    }
}
