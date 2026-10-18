using System.Windows;
using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public string VersionText { get; } =
        "OpenWindows " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.9.0");

    public string AdminStatusText => ElevationHelper.IsAdmin
        ? "Running as administrator. Service, task and machine-wide registry fixes are all available."
        : "Running as a standard user. Service, scheduled task and machine-wide fixes are marked [admin] and skipped.";

    public bool IsAdmin => ElevationHelper.IsAdmin;

    public RelayCommand RestartAsAdminCommand { get; }

    public RelayCommand TestToastCommand { get; }

    public RelayCommand OpenLogCommand { get; }

    public RelayCommand OpenBackupsCommand { get; }

    public RelayCommand OpenDisabledStartupCommand { get; }

    public RelayCommand OpenRepoCommand { get; }

    public SettingsViewModel()
    {
        RestartAsAdminCommand = new RelayCommand(RestartAsAdmin);
        TestToastCommand = new RelayCommand(() =>
            AppServices.Notifications.Show("OpenWindows", "This is what a notification looks like. Nice, right?"));
        OpenLogCommand = new RelayCommand(() =>
        {
            AppPaths.EnsureAppDir();
            if (!File.Exists(AppPaths.LogFile))
            {
                File.WriteAllText(AppPaths.LogFile, "OpenWindows log - no entries yet." + Environment.NewLine);
            }
            OpenInExplorer(AppPaths.LogFile);
        });
        OpenBackupsCommand = new RelayCommand(() =>
        {
            AppPaths.EnsureBackupDir();
            OpenInExplorer(AppPaths.BackupDir);
        });
        OpenDisabledStartupCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.DisabledStartupDir);
            OpenInExplorer(AppPaths.DisabledStartupDir);
        });
        OpenRepoCommand = new RelayCommand(() =>
            OpenInExplorer("https://github.com/kalibpik4-max/OpenWindows"));
    }

    public bool ThemeIsSystem
    {
        get => AppServices.Settings.Current.Theme == "System";
        set { if (value) ApplyTheme("System"); }
    }

    public bool ThemeIsLight
    {
        get => AppServices.Settings.Current.Theme == "Light";
        set { if (value) ApplyTheme("Light"); }
    }

    public bool ThemeIsDark
    {
        get => AppServices.Settings.Current.Theme == "Dark";
        set { if (value) ApplyTheme("Dark"); }
    }

    public bool CpuAlertsEnabled
    {
        get => AppServices.Settings.Current.CpuAlertsEnabled;
        set
        {
            AppServices.Settings.Current.CpuAlertsEnabled = value;
            PersistAndSync();
            OnPropertyChanged();
        }
    }

    public double AlertThreshold
    {
        get => AppServices.Settings.Current.CpuAlertThreshold;
        set
        {
            var rounded = Math.Round(value);
            if (Math.Abs(AppServices.Settings.Current.CpuAlertThreshold - rounded) < 0.5)
            {
                return;
            }
            AppServices.Settings.Current.CpuAlertThreshold = (int)rounded;
            PersistAndSync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AlertThresholdText));
        }
    }

    public string AlertThresholdText => $"{(int)AlertThreshold}% CPU";

    public bool ToastNotificationsEnabled
    {
        get => AppServices.Settings.Current.ToastNotificationsEnabled;
        set
        {
            AppServices.Settings.Current.ToastNotificationsEnabled = value;
            if (value && !AppServices.Notifications.ToastRegistered)
            {
                AppServices.Notifications.Register();
            }
            PersistAndSync();
            OnPropertyChanged();
        }
    }

    public bool MinimizeToTray
    {
        get => AppServices.Settings.Current.MinimizeToTray;
        set
        {
            AppServices.Settings.Current.MinimizeToTray = value;
            PersistAndSync();
            OnPropertyChanged();
        }
    }

    public bool RunAtStartup
    {
        get => AppServices.Settings.Current.RunAtStartup;
        set
        {
            AppServices.Settings.Current.RunAtStartup = value;
            try
            {
                if (value)
                {
                    AutostartService.Enable();
                }
                else
                {
                    AutostartService.Disable();
                }
            }
            catch (Exception ex)
            {
                Log.Write("Autostart change failed: " + ex.Message);
            }
            PersistAndSync();
            OnPropertyChanged();
        }
    }

    public bool MonitoringAutoStart
    {
        get => AppServices.Settings.Current.MonitoringAutoStart;
        set
        {
            AppServices.Settings.Current.MonitoringAutoStart = value;
            PersistAndSync();
            OnPropertyChanged();
        }
    }

    public bool BackgroundAlertsEnabled
    {
        get => AppServices.Settings.Current.BackgroundAlertsEnabled;
        set
        {
            AppServices.Settings.Current.BackgroundAlertsEnabled = value;
            PersistAndSync();
            OnPropertyChanged();
        }
    }

    private void ApplyTheme(string theme)
    {
        AppServices.Settings.Current.Theme = theme;
        AppServices.Settings.Save();
        ThemeService.Apply(theme, Application.Current.MainWindow);
        OnPropertyChanged(nameof(ThemeIsSystem));
        OnPropertyChanged(nameof(ThemeIsLight));
        OnPropertyChanged(nameof(ThemeIsDark));
    }

    private static void PersistAndSync()
    {
        AppServices.Settings.Save();
        AppServices.ApplySettingsToServices();
    }

    private static void RestartAsAdmin()
    {
        if (ElevationHelper.RelaunchElevated())
        {
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Write("Open failed for " + path + ": " + ex.Message);
        }
    }
}
