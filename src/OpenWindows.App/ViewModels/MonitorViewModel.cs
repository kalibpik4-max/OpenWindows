using System.Collections.ObjectModel;
using System.Windows;
using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public sealed class MonitorViewModel : ViewModelBase
{
    private string cpuText = "-";
    private string ramText = "-";
    private string diskText = "-";
    private string powerPlanText = "Power plan: checking...";
    private bool isMonitoring;

    public ObservableCollection<double> CpuValues { get; } = new();

    public ObservableCollection<double> RamValues { get; } = new();

    public ObservableCollection<double> DiskValues { get; } = new();

    public ObservableCollection<ProcessRowVM> Processes { get; } = new();

    public RelayCommand ToggleMonitoringCommand { get; }

    public RelayCommand RefreshPowerPlanCommand { get; }

    public AsyncRelayCommand<ProcessRowVM> EndProcessCommand { get; }

    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public MonitorViewModel()
    {
        ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
        RefreshPowerPlanCommand = new RelayCommand(RefreshPowerPlan);
        EndProcessCommand = new AsyncRelayCommand<ProcessRowVM>(EndProcessAsync);

        isMonitoring = AppServices.Monitor.IsRunning;
        AppServices.Monitor.SampleReady += OnSampleReady;
        AppServices.Monitor.CpuAlert += OnCpuAlert;

        foreach (var sample in AppServices.Monitor.History)
        {
            AppendPoint(sample);
        }
        if (AppServices.Monitor.Latest is { } latest)
        {
            UpdateTexts(latest);
            UpdateProcessRows(latest);
        }

        RefreshPowerPlan();
    }

    public bool IsMonitoring
    {
        get => isMonitoring;
        private set
        {
            if (SetProperty(ref isMonitoring, value))
            {
                OnPropertyChanged(nameof(MonitoringButtonText));
            }
        }
    }

    public string MonitoringButtonText => IsMonitoring ? "Pause monitoring" : "Start monitoring";

    public string CpuText
    {
        get => cpuText;
        private set => SetProperty(ref cpuText, value);
    }

    public string RamText
    {
        get => ramText;
        private set => SetProperty(ref ramText, value);
    }

    public string DiskText
    {
        get => diskText;
        private set => SetProperty(ref diskText, value);
    }

    public string PowerPlanText
    {
        get => powerPlanText;
        private set => SetProperty(ref powerPlanText, value);
    }

    public bool AlertsEnabled
    {
        get => AppServices.Settings.Current.CpuAlertsEnabled;
        set
        {
            AppServices.Settings.Current.CpuAlertsEnabled = value;
            AppServices.Settings.Save();
            AppServices.ApplySettingsToServices();
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
            AppServices.Settings.Save();
            AppServices.ApplySettingsToServices();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AlertThresholdText));
        }
    }

    public string AlertThresholdText => $"{(int)AlertThreshold}% CPU";

    private void ToggleMonitoring()
    {
        if (AppServices.Monitor.IsRunning)
        {
            AppServices.Monitor.Stop();
        }
        else
        {
            AppServices.Monitor.Start();
        }
        IsMonitoring = AppServices.Monitor.IsRunning;
    }

    public void SyncMonitoringState()
    {
        IsMonitoring = AppServices.Monitor.IsRunning;
    }

    private void RefreshPowerPlan()
    {
        try
        {
            var (exitCode, output, _) = ProcessRunner.Run("powercfg", "/getactivescheme");
            if (exitCode == 0)
            {
                var text = output.Trim();
                var index = text.IndexOf('(');
                PowerPlanText = "Power plan: " + (index >= 0 ? text[(index + 1)..].TrimEnd(')', ' ', '\r', '\n') : text);
                return;
            }
        }
        catch
        {
        }
        PowerPlanText = "Power plan: unknown";
    }

    private void OnSampleReady(object? sender, MonitorSample sample)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }
        dispatcher.BeginInvoke(() =>
        {
            AppendPoint(sample);
            UpdateTexts(sample);
            UpdateProcessRows(sample);
            IsMonitoring = true;
        });
    }

    private void UpdateProcessRows(MonitorSample sample)
    {
        Processes.Clear();
        foreach (var process in sample.TopProcesses)
        {
            Processes.Add(new ProcessRowVM
            {
                Name = process.Name,
                Id = process.Id,
                CpuPercent = process.CpuPercent,
                RamMb = process.RamMb,
                HasWindow = process.HasWindow
            });
        }
    }

    private async Task EndProcessAsync(ProcessRowVM row)
    {
        if (row == null || ProtectedProcesses.IsProtected(row.Name))
        {
            return;
        }
        if (ConfirmAsync != null)
        {
            var confirmed = await ConfirmAsync("End process",
                $"End {row.Name} (PID {row.Id})? Unsaved work in it will be lost.");
            if (!confirmed)
            {
                return;
            }
        }

        if (ProcessScanService.TryKill(row.Id, row.Name, out var message))
        {
            AppServices.NotifyUser("Process ended", message, true);
        }
        else
        {
            AppServices.NotifyUser("Could not end process", message, false);
        }
    }

    private void AppendPoint(MonitorSample sample)
    {
        AddBounded(CpuValues, sample.CpuPercent);
        AddBounded(RamValues, sample.RamPercent);
        AddBounded(DiskValues, sample.DiskPercent);
    }

    private static void AddBounded(ObservableCollection<double> collection, double value)
    {
        collection.Add(value);
        while (collection.Count > 120)
        {
            collection.RemoveAt(0);
        }
    }

    private void UpdateTexts(MonitorSample sample)
    {
        CpuText = $"{sample.CpuPercent:0}%";
        RamText = $"{sample.RamUsedGb:N1} / {sample.RamTotalGb:N1} GB ({sample.RamPercent:0}%)";
        DiskText = $"{sample.DiskFreeGb:N0} GB free of {sample.DiskTotalGb:N0} GB";
    }

    private void OnCpuAlert(object? sender, CpuAlertInfo info)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }
        dispatcher.BeginInvoke(() =>
        {
            AppServices.Notifications.Show("High CPU usage",
                $"CPU has been above {info.Threshold}% for 3+ checks. Top process: {info.TopProcess} ({info.TopProcessPercent:0.0}%)");
        });
    }
}
