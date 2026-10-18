using System.Text.Json;

namespace OpenWindows.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool CpuAlertsEnabled { get; set; } = true;
    public int CpuAlertThreshold { get; set; } = 85;
    public bool MinimizeToTray { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public bool ToastNotificationsEnabled { get; set; } = true;
    public bool MonitoringAutoStart { get; set; } = true;
    public bool BackgroundAlertsEnabled { get; set; } = true;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Write("Settings load failed: " + ex.Message);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureAppDir();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, Options));
        }
        catch (Exception ex)
        {
            Log.Write("Settings save failed: " + ex.Message);
        }
    }
}
