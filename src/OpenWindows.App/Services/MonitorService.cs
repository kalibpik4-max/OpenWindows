using System.Diagnostics;

namespace OpenWindows.Services;

public sealed record ProcessInfoSnapshot(string Name, int Id, double CpuPercent, long RamMb, bool HasWindow);

public sealed record BackgroundHogInfo(string Name, int Id, double CpuPercent, long RamMb);

public sealed record MonitorSample(
    DateTime Timestamp,
    double CpuPercent,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double DiskPercent,
    double DiskFreeGb,
    double DiskTotalGb,
    IReadOnlyList<ProcessInfoSnapshot> TopProcesses);

public sealed record CpuAlertInfo(double CpuPercent, string TopProcess, double TopProcessPercent, int Threshold);

public sealed class MonitorService : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<MonitorSample> history = new();
    private readonly Dictionary<int, double> lastProcessCpuMs = new();
    private readonly Dictionary<string, int> hogStreaks = new();
    private readonly Dictionary<string, DateTime> lastHogAlert = new();

    private CancellationTokenSource? cancellation;
    private Task? loopTask;
    private DateTime lastTick = DateTime.MinValue;
    private ulong lastIdle;
    private ulong lastKernel;
    private ulong lastUser;
    private bool hasSystemTimes;
    private int streak;
    private DateTime lastAlert = DateTime.MinValue;

    private const int HistoryLimit = 150;
    private const int IntervalMs = 2000;
    private const double HogCpuThreshold = 6.0;
    private const int HogStreakLimit = 5;
    private static readonly TimeSpan HogCooldown = TimeSpan.FromMinutes(15);

    public bool IsRunning { get; private set; }

    public bool AlertsEnabled { get; set; } = true;

    public bool BackgroundAlertsEnabled { get; set; } = true;

    public int AlertThreshold { get; set; } = 85;

    public event EventHandler<MonitorSample>? SampleReady;

    public event EventHandler<CpuAlertInfo>? CpuAlert;

    public event EventHandler<BackgroundHogInfo>? BackgroundHogDetected;

    public IReadOnlyList<MonitorSample> History
    {
        get
        {
            lock (gate)
            {
                return history.ToArray();
            }
        }
    }

    public MonitorSample? Latest
    {
        get
        {
            lock (gate)
            {
                return history.Count > 0 ? history.Last() : null;
            }
        }
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }
        IsRunning = true;
        streak = 0;
        hasSystemTimes = false;
        lastProcessCpuMs.Clear();
        hogStreaks.Clear();
        lastTick = DateTime.MinValue;
        cancellation = new CancellationTokenSource();
        loopTask = Task.Run(() => LoopAsync(cancellation.Token));
        Log.Write("Monitoring started.");
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }
        IsRunning = false;
        try
        {
            cancellation?.Cancel();
        }
        catch
        {
        }
        Log.Write("Monitoring stopped.");
    }

    private async Task LoopAsync(CancellationToken token)
    {
        SampleOnce();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(IntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    SampleOnce();
                }
                catch (Exception ex)
                {
                    Log.Write("Monitor tick failed: " + ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SampleOnce()
    {
        var now = DateTime.Now;
        var cores = Environment.ProcessorCount;

        var cpuPercent = ReadSystemCpuPercent();

        var (ramPercent, ramUsedGb, ramTotalGb) = SystemInfoService.GetRamUsage();
        var (diskPercent, diskFreeGb, diskTotalGb) = SystemInfoService.GetDiskUsage("C");

        var elapsedMs = lastTick == DateTime.MinValue ? IntervalMs : (now - lastTick).TotalMilliseconds;
        elapsedMs = Math.Max(250, elapsedMs);

        var processes = new List<ProcessInfoSnapshot>();
        var nextCpuMs = new Dictionary<int, double>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var cpuMs = process.TotalProcessorTime.TotalMilliseconds;
                nextCpuMs[process.Id] = cpuMs;
                if (lastProcessCpuMs.TryGetValue(process.Id, out var previousMs))
                {
                    var percent = (cpuMs - previousMs) / (elapsedMs * cores) * 100;
                    if (percent >= 0.5)
                    {
                        var hasWindow = false;
                        try
                        {
                            hasWindow = process.MainWindowHandle != IntPtr.Zero;
                        }
                        catch
                        {
                        }

                        processes.Add(new ProcessInfoSnapshot(
                            process.ProcessName,
                            process.Id,
                            Math.Round(percent, 1),
                            process.WorkingSet64 / 1024 / 1024,
                            hasWindow));
                    }
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        lastProcessCpuMs.Clear();
        foreach (var (pid, ms) in nextCpuMs)
        {
            lastProcessCpuMs[pid] = ms;
        }
        lastTick = now;
        hasSystemTimes = true;

        var top = processes.OrderByDescending(p => p.CpuPercent).Take(12).ToList();

        var sample = new MonitorSample(
            now, cpuPercent, ramPercent, ramUsedGb, ramTotalGb,
            diskPercent, diskFreeGb, diskTotalGb, top);

        lock (gate)
        {
            history.Enqueue(sample);
            while (history.Count > HistoryLimit)
            {
                history.Dequeue();
            }
        }

        SampleReady?.Invoke(this, sample);

        if (AlertsEnabled && sample.CpuPercent >= AlertThreshold)
        {
            streak++;
        }
        else
        {
            streak = 0;
        }

        if (streak >= 3 && (now - lastAlert).TotalSeconds >= 60)
        {
            lastAlert = now;
            var topProcess = top.Count > 0 ? top[0] : null;
            CpuAlert?.Invoke(this, new CpuAlertInfo(
                sample.CpuPercent,
                topProcess?.Name ?? "unknown",
                topProcess?.CpuPercent ?? 0,
                AlertThreshold));
        }

        if (BackgroundAlertsEnabled)
        {
            EvaluateBackgroundHogs(processes, now);
        }
    }

    private void EvaluateBackgroundHogs(List<ProcessInfoSnapshot> processes, DateTime now)
    {
        try
        {
            var hogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in processes)
            {
                if (process.HasWindow || process.CpuPercent < HogCpuThreshold)
                {
                    continue;
                }
                if (ProtectedProcesses.IsProtected(process.Name))
                {
                    continue;
                }
                hogs.Add(process.Name);
                hogStreaks[process.Name] = hogStreaks.TryGetValue(process.Name, out var current) ? current + 1 : 1;

                if (hogStreaks[process.Name] < HogStreakLimit)
                {
                    continue;
                }
                if (lastHogAlert.TryGetValue(process.Name, out var last) && now - last < HogCooldown)
                {
                    continue;
                }

                lastHogAlert[process.Name] = now;
                hogStreaks[process.Name] = 0;
                BackgroundHogDetected?.Invoke(this,
                    new BackgroundHogInfo(process.Name, process.Id, process.CpuPercent, process.RamMb));
            }

            foreach (var name in hogStreaks.Keys.Where(n => !hogs.Contains(n)).ToList())
            {
                hogStreaks.Remove(name);
            }

            if (lastHogAlert.Count > 200)
            {
                foreach (var name in lastHogAlert.Where(p => now - p.Value > TimeSpan.FromHours(2)).Select(p => p.Key).ToList())
                {
                    lastHogAlert.Remove(name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Background hog evaluation failed: " + ex.Message);
        }
    }

    private double ReadSystemCpuPercent()
    {
        try
        {
            if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return 0;
            }
            var idleTicks = idle.ToUlong();
            var kernelTicks = kernel.ToUlong();
            var userTicks = user.ToUlong();

            if (!hasSystemTimes)
            {
                lastIdle = idleTicks;
                lastKernel = kernelTicks;
                lastUser = userTicks;
                return 0;
            }

            var idleDelta = idleTicks - lastIdle;
            var totalDelta = (kernelTicks - lastKernel) + (userTicks - lastUser);
            lastIdle = idleTicks;
            lastKernel = kernelTicks;
            lastUser = userTicks;

            if (totalDelta == 0)
            {
                return 0;
            }
            var percent = (1.0 - (double)idleDelta / totalDelta) * 100.0;
            return Math.Round(Math.Clamp(percent, 0, 100), 1);
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        Stop();
        try
        {
            loopTask?.Wait(1000);
        }
        catch
        {
        }
    }
}
