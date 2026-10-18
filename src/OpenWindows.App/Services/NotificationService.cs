using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace OpenWindows.Services;

public sealed class NotificationService
{
    private Action<string, string>? balloonFallback;

    public bool ToastRegistered { get; private set; }

    public bool Enabled { get; set; } = true;

    public event EventHandler? Activated;

    public void SetFallback(Action<string, string> fallback) => balloonFallback = fallback;

    public void Register()
    {
        if (ToastRegistered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        }
        catch (Exception ex)
        {
            Log.Write("Toast handler attach failed: " + ex.Message);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                AppNotificationManager.Default.Register();
                ToastRegistered = true;
                Log.Write("Toast notifications registered.");
                return;
            }
            catch (Exception ex)
            {
                if (attempt == 0 && TryProvideInsightsResource())
                {
                    continue;
                }
                ToastRegistered = false;
                Log.Write("Toast registration failed, falling back to tray balloons: " + ex.Message);
            }
        }
    }

    private static bool TryProvideInsightsResource()
    {
        try
        {
            const string fileName = "Microsoft.WindowsAppRuntime.Insights.Resource.dll";
            var target = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(target))
            {
                return false;
            }

            var source = FindInsightsResourceInRuntimePackage(fileName);
            if (source == null)
            {
                return false;
            }

            File.Copy(source, target);
            Log.Write("Copied WindowsAppRuntime Insights resource DLL for toast support.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Insights resource bootstrap failed: " + ex.Message);
            return false;
        }
    }

    private static string? FindInsightsResourceInRuntimePackage(string fileName)
    {
        try
        {
            var windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (Directory.Exists(windowsApps))
            {
                var direct = Directory
                    .EnumerateDirectories(windowsApps, "Microsoft.WindowsAppRuntime.2_*")
                    .Select(dir => Path.Combine(dir, fileName))
                    .FirstOrDefault(File.Exists);
                if (direct != null)
                {
                    return direct;
                }
            }
        }
        catch
        {
        }

        try
        {
            var script =
                "(Get-AppxPackage -Name Microsoft.WindowsAppRuntime.2 | Sort-Object Version -Descending | Select-Object -First 1).InstallLocation";
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                ArgumentList =
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    script
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                return null;
            }
            var installLocation = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(8000);
            if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
            {
                return null;
            }
            var candidate = Path.Combine(installLocation, fileName);
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception ex)
        {
            Log.Write("Runtime package lookup failed: " + ex.Message);
            return null;
        }
    }

    public void Unregister()
    {
        if (!ToastRegistered)
        {
            return;
        }
        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            Log.Write("Toast unregister failed: " + ex.Message);
        }
        ToastRegistered = false;
    }

    public void Show(string title, string body)
    {
        if (!Enabled)
        {
            return;
        }

        if (ToastRegistered)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification();
                AppNotificationManager.Default.Show(notification);
                return;
            }
            catch (Exception ex)
            {
                Log.Write("Toast show failed: " + ex.Message);
            }
        }

        try
        {
            balloonFallback?.Invoke(title, body);
        }
        catch
        {
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        Activated?.Invoke(this, EventArgs.Empty);
    }
}
