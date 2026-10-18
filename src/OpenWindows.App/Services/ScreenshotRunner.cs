using System.Windows.Media.Imaging;
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
