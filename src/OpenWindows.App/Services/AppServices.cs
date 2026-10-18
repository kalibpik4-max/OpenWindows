using Wpf.Ui;
using Wpf.Ui.Controls;

namespace OpenWindows.Services;

public static class AppServices
{
    public static SettingsService Settings { get; } = new();

    public static NotificationService Notifications { get; } = new();

    public static MonitorService Monitor { get; } = new();

    public static ScanCoordinator Scanner { get; } = new();

    public static FixEngine Fixer { get; } = new();

    public static HistoryService History { get; } = new();

    public static SnackbarService Snackbars { get; } = new();

    public static void Initialize()
    {
        Settings.Load();
        Monitor.AlertsEnabled = Settings.Current.CpuAlertsEnabled;
        Monitor.AlertThreshold = Settings.Current.CpuAlertThreshold;
        Notifications.Enabled = Settings.Current.ToastNotificationsEnabled;
        Log.Write($"OpenWindows started. Admin: {ElevationHelper.IsAdmin}");
    }

    public static void NotifyUser(string title, string message, bool success = true)
    {
        try
        {
            Snackbars.Show(
                title,
                message,
                success ? ControlAppearance.Success : ControlAppearance.Caution,
                new SymbolIcon(success ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.Warning24),
                TimeSpan.FromSeconds(4));
        }
        catch
        {
        }
        Notifications.Show(title, message);
    }

    public static void ApplySettingsToServices()
    {
        Monitor.AlertsEnabled = Settings.Current.CpuAlertsEnabled;
        Monitor.AlertThreshold = Settings.Current.CpuAlertThreshold;
        Notifications.Enabled = Settings.Current.ToastNotificationsEnabled;
    }

    public static void Shutdown()
    {
        try
        {
            Monitor.Stop();
            Notifications.Unregister();
            Settings.Save();
        }
        catch
        {
        }
    }
}
