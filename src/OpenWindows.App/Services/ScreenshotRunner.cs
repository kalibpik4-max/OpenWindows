using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using OpenWindows.ViewModels;

namespace OpenWindows.Services;

public static class ScreenshotRunner
{
    public static void Start(Window window, string directory, string? pagesCsv)
    {
        var pages = (pagesCsv ?? "Dashboard")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                await Task.Delay(2500);
                foreach (var page in pages)
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        if (Enum.TryParse<AppPage>(page, true, out var parsed))
                        {
                            if (window is MainWindow main)
                            {
                                main.NavigateTo(parsed);
                            }
                        }
                    });
                    await Task.Delay(2600);
                    if (string.Equals(page, "Optimize", StringComparison.OrdinalIgnoreCase))
                    {
                        window.Dispatcher.Invoke(() => ClickScanButton(window));
                        await Task.Delay(16000);
                    }
                    var target = Path.Combine(directory, "shot-" + page.ToLowerInvariant() + ".png");
                    window.Dispatcher.Invoke(() => Capture(window, target));
                    Log.Write("Screenshot saved: " + target);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Screenshot run failed: " + ex.Message);
            }
        });
    }

    private static void ClickScanButton(Window window)
    {
        var button = FindDescendant<Button>(window, b => b.Content is "Scan my PC");
        if (button == null)
        {
            return;
        }
        var peer = new ButtonAutomationPeer(button);
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && match(typed))
            {
                return typed;
            }
            var found = FindDescendant(child, match);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static void Capture(Window window, string path)
    {
        if (window.Content is not FrameworkElement root || root.ActualWidth <= 0 || root.ActualHeight <= 0)
        {
            return;
        }

        var w = root.ActualWidth;
        var h = root.ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(w * dpi.DpiScaleX),
            (int)Math.Ceiling(h * dpi.DpiScaleY),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), null, new Rect(0, 0, w, h));
            context.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None }, null, new Rect(0, 0, w, h));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
