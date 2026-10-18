using System.Collections.ObjectModel;
using System.Windows;
using OpenWindows.Models;
using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly SystemSpecs specs;
    private int healthScore = -1;
    private string lastScanText = "No scan yet.";
    private bool isMonitoring;
    private bool unusedBusy;
    private DateTime lastUnusedRefresh = DateTime.MinValue;

    public ObservableCollection<FindingItemVM> RecentFindings { get; } = new();

    public ObservableCollection<double> CpuHistory { get; } = new();

    public ObservableCollection<double> RamHistory { get; } = new();

    public ObservableCollection<ProcessItemVM> UnusedProcesses { get; } = new();

    public event Action<AppPage>? NavigateRequested;

    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public RelayCommand ScanNowCommand { get; }

    public RelayCommand OpenMonitorCommand { get; }

    public RelayCommand OpenHistoryCommand { get; }

    public RelayCommand OpenProcessesCommand { get; }

    public AsyncRelayCommand UndoLastCommand { get; }

    public DashboardViewModel()
    {
        specs = SystemInfoService.GetSpecs();
        ScanNowCommand = new RelayCommand(() =>
        {
            OptimizePageRequest.ScanPending = true;
            NavigateRequested?.Invoke(AppPage.Optimize);
        });
        OpenMonitorCommand = new RelayCommand(() =>
        {
            if (!AppServices.Monitor.IsRunning)
            {
                AppServices.Monitor.Start();
            }
            IsMonitoring = true;
            NavigateRequested?.Invoke(AppPage.Monitor);
        });
        OpenHistoryCommand = new RelayCommand(() => NavigateRequested?.Invoke(AppPage.History));
        OpenProcessesCommand = new RelayCommand(() => NavigateRequested?.Invoke(AppPage.Processes));
        UndoLastCommand = new AsyncRelayCommand(UndoLastAsync);

        foreach (var sample in AppServices.Monitor.History)
        {
            AddBounded(CpuHistory, sample.CpuPercent);
            AddBounded(RamHistory, sample.RamPercent);
        }

        AppServices.Monitor.SampleReady += OnSampleReady;
        SyncMonitoringState();
        RefreshFromLastScan();
    }

    public bool IsMonitoring
    {
        get => isMonitoring;
        private set
        {
            if (SetProperty(ref isMonitoring, value))
            {
                OnPropertyChanged(nameof(MonitorButtonText));
            }
        }
    }

    public string MonitorButtonText => IsMonitoring ? "Open monitor" : "Start monitoring";

    public void SyncMonitoringState()
    {
        IsMonitoring = AppServices.Monitor.IsRunning;
    }

    private string cpuLevel = "Low";
    public string CpuLevel
    {
        get => cpuLevel;
        private set => SetProperty(ref cpuLevel, value);
    }

    private string ramLevel = "Low";
    public string RamLevel
    {
        get => ramLevel;
        private set => SetProperty(ref ramLevel, value);
    }

    private string diskLevel = "Low";
    public string DiskLevel
    {
        get => diskLevel;
        private set => SetProperty(ref diskLevel, value);
    }

    public bool HasUnused => UnusedProcesses.Count > 0;

    private string unusedSummaryText = "";
    public string UnusedSummaryText
    {
        get => unusedSummaryText;
        private set => SetProperty(ref unusedSummaryText, value);
    }

    public async Task RefreshUnusedAsync(bool force)
    {
        if (unusedBusy)
        {
            return;
        }
        if (!force && (DateTime.Now - lastUnusedRefresh).TotalSeconds < 15)
        {
            return;
        }
        unusedBusy = true;
        try
        {
            var snapshot = await Task.Run(() => ProcessScanService.Snapshot());
            var unused = snapshot
                .Where(p => ProcessScanService.IsUnused(p))
                .OrderByDescending(ProcessScanService.Score)
                .Take(3)
                .Select(p => new ProcessItemVM(p))
                .ToList();

            UnusedProcesses.Clear();
            foreach (var item in unused)
            {
                UnusedProcesses.Add(item);
            }

            var allUnused = snapshot.Where(p => ProcessScanService.IsUnused(p)).ToList();
            var reclaimable = allUnused.Sum(p => p.RamMb) / 1024.0;
            UnusedSummaryText = allUnused.Count == 0
                ? ""
                : $"{allUnused.Count} app(s) are running in the background without being used - about {reclaimable:0.0} GB of memory can be freed.";
            OnPropertyChanged(nameof(HasUnused));
            lastUnusedRefresh = DateTime.Now;
        }
        catch (Exception ex)
        {
            Log.Write("Unused app refresh failed: " + ex.Message);
        }
        finally
        {
            unusedBusy = false;
        }
    }

    public string CpuName => specs.CpuName;

    public string CpuDetail => $"{specs.Cores} cores / {specs.Threads} threads - max {specs.MaxClockMhz} MHz";

    public string OsDetail => string.IsNullOrEmpty(specs.OsVersion)
        ? $"{specs.OsName} (build {specs.OsBuild})"
        : $"{specs.OsName} {specs.OsVersion} (build {specs.OsBuild})";

    public string UptimeDetail => "Uptime: " + FormatUptime();

    private string cpuPercentText = "-";
    public string CpuPercentText
    {
        get => cpuPercentText;
        private set => SetProperty(ref cpuPercentText, value);
    }

    private double cpuPercent;
    public double CpuPercent
    {
        get => cpuPercent;
        private set => SetProperty(ref cpuPercent, value);
    }

    private string ramText = "-";
    public string RamText
    {
        get => ramText;
        private set => SetProperty(ref ramText, value);
    }

    private double ramPercent;
    public double RamPercent
    {
        get => ramPercent;
        private set => SetProperty(ref ramPercent, value);
    }

    private string diskText = "-";
    public string DiskText
    {
        get => diskText;
        private set => SetProperty(ref diskText, value);
    }

    private double diskPercent;
    public double DiskPercent
    {
        get => diskPercent;
        private set => SetProperty(ref diskPercent, value);
    }

    public bool HasHealthScore => healthScore >= 0;

    public string HealthScoreText => healthScore < 0 ? "-" : healthScore.ToString();

    public string HealthLabel => healthScore switch
    {
        < 0 => "Not scanned yet",
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= 50 => "Fair",
        _ => "Needs attention"
    };

    public string HealthDescription => healthScore switch
    {
        < 0 => "Run a scan and the health score appears here.",
        >= 90 => "This PC is in great shape. Keep it that way with the occasional scan.",
        >= 75 => "Solid overall. A few fixes below could free up CPU, RAM or disk.",
        >= 50 => "There is real room for improvement - apply the safe fixes below to boost this score.",
        _ => "Plenty of easy wins in the findings - apply the safe ones and watch this score climb."
    };

    public string LastScanText
    {
        get => lastScanText;
        private set => SetProperty(ref lastScanText, value);
    }

    public int OpenIssueCount { get; private set; }

    public bool HasRecentFindings => RecentFindings.Count > 0;

    public void RefreshFromLastScan()
    {
        var findings = AppState.LastScanFindings;
        RecentFindings.Clear();
        if (findings == null)
        {
            healthScore = -1;
            OpenIssueCount = 0;
            LastScanText = "";
        }
        else
        {
            var actionables = findings.Where(f => f.Action != null).ToList();
            var safe = actionables.Count(f => f.Risk == RiskLevel.Safe);
            var caution = actionables.Count(f => f.Risk == RiskLevel.Caution);
            var (_, diskFreeGb, diskTotalGb) = SystemInfoService.GetDiskUsage("C");
            var diskFreePercent = diskTotalGb > 0 ? diskFreeGb / diskTotalGb * 100 : 100;
            var penalty = safe * 1 + caution * 2 + (diskFreePercent < 20 ? 8 : 0);
            healthScore = Math.Clamp(100 - (int)penalty, 25, 100);
            OpenIssueCount = actionables.Count;
            LastScanText = $"Last scan: {AppState.LastScanTime:g} - {OpenIssueCount} actionable findings.";

            foreach (var finding in findings.Where(f => f.Action != null).Take(6))
            {
                RecentFindings.Add(new FindingItemVM(finding));
            }
        }

        OnPropertyChanged(nameof(HealthScoreText));
        OnPropertyChanged(nameof(HasHealthScore));
        OnPropertyChanged(nameof(HealthLabel));
        OnPropertyChanged(nameof(HealthDescription));
        OnPropertyChanged(nameof(OpenIssueCount));
        OnPropertyChanged(nameof(HasRecentFindings));
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
            CpuPercent = sample.CpuPercent;
            CpuPercentText = $"{sample.CpuPercent:0}%";
            CpuLevel = Level(sample.CpuPercent, 85, 60);
            AddBounded(CpuHistory, sample.CpuPercent);
            RamPercent = sample.RamPercent;
            RamText = $"{sample.RamUsedGb:N1} GB of {sample.RamTotalGb:N1} GB";
            RamLevel = Level(sample.RamPercent, 85, 70);
            AddBounded(RamHistory, sample.RamPercent);
            DiskPercent = sample.DiskPercent;
            DiskText = $"{sample.DiskFreeGb:N1} GB free of {sample.DiskTotalGb:N0} GB";
            DiskLevel = Level(sample.DiskPercent, 90, 80);
            IsMonitoring = true;
            _ = RefreshUnusedAsync(false);
        });
    }

    private static string Level(double value, double high, double medium)
    {
        return value >= high ? "High" : value >= medium ? "Medium" : "Low";
    }

    private static void AddBounded(ObservableCollection<double> collection, double value)
    {
        collection.Add(value);
        while (collection.Count > 120)
        {
            collection.RemoveAt(0);
        }
    }

    private async Task UndoLastAsync()
    {
        if (ConfirmAsync != null)
        {
            var confirmed = await ConfirmAsync("Undo last changes",
                "Undo the most recent apply session? This restores registry values, service startup types, scheduled tasks, startup shortcuts, and the power plan.");
            if (!confirmed)
            {
                return;
            }
        }

        var result = await Task.Run(() => AppServices.Fixer.UndoLatest());
        AppServices.Notifications.Show("OpenWindows",
            result.Note ?? $"Restored {result.Count} change(s), {result.Failed} failed.");
    }

    private static string FormatUptime()
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (uptime.TotalDays >= 1)
            {
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            }
            return $"{uptime.Hours}h {uptime.Minutes}m";
        }
        catch
        {
            return "unknown";
        }
    }
}

public static class AppState
{
    public static List<Finding>? LastScanFindings { get; set; }
    public static DateTime LastScanTime { get; set; }
}

public static class OptimizePageRequest
{
    public static bool ScanPending { get; set; }
}
