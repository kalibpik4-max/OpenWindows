using System.Diagnostics;

namespace OpenWindows.Services;

public sealed record AppProcessInfo(
    int Id,
    string Name,
    string Title,
    bool HasWindow,
    bool IsProtected,
    double CpuPercent,
    long RamMb);

public static class ProcessScanService
{
    private sealed record RawSample(string Name, double CpuMs, long RamMb, bool HasWindow, string Title);

    public static List<AppProcessInfo> Snapshot(int sampleMilliseconds = 700)
    {
        var cores = Environment.ProcessorCount;
        var first = Sample();
        Thread.Sleep(sampleMilliseconds);
        var second = Sample();

        var result = new List<AppProcessInfo>(second.Count);
        foreach (var (pid, current) in second)
        {
            var percent = 0.0;
            if (first.TryGetValue(pid, out var previous))
            {
                var delta = current.CpuMs - previous.CpuMs;
                percent = Math.Round(Math.Max(0, delta) / (sampleMilliseconds * (double)cores) * 100, 1);
            }

            result.Add(new AppProcessInfo(
                pid,
                current.Name,
                current.Title,
                current.HasWindow,
                ProtectedProcesses.IsProtected(current.Name),
                percent,
                current.RamMb));
        }

        var namesWithWindows = result
            .Where(p => p.HasWindow)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < result.Count; i++)
        {
            var item = result[i];
            if (!item.HasWindow && namesWithWindows.Contains(item.Name))
            {
                result[i] = item with { HasWindow = true };
            }
        }

        return result;
    }

    public static bool IsUnused(AppProcessInfo process)
    {
        return !process.IsProtected
            && !process.HasWindow
            && (process.CpuPercent >= 1.5 || process.RamMb >= 200);
    }

    public static double Score(AppProcessInfo process)
    {
        return (IsUnused(process) ? 1_000_000 : 0) + process.CpuPercent * 1000 + process.RamMb;
    }

    public static bool TryKill(int id, string name, out string message)
    {
        try
        {
            var process = Process.GetProcessById(id);
            using (process)
            {
                if (!string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                {
                    message = "PID now belongs to a different process - skipped for safety";
                    return false;
                }
                process.Kill();
            }
            Log.Write($"Ended process {name} (PID {id})");
            message = "Ended " + name;
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"End process failed for {name} (PID {id}): {ex.Message}");
            message = ex.Message;
            return false;
        }
    }

    private static Dictionary<int, RawSample> Sample()
    {
        var result = new Dictionary<int, RawSample>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var hasWindow = false;
                var title = "";
                try
                {
                    hasWindow = process.MainWindowHandle != IntPtr.Zero;
                    if (hasWindow)
                    {
                        title = process.MainWindowTitle ?? "";
                    }
                }
                catch
                {
                }

                result[process.Id] = new RawSample(
                    process.ProcessName,
                    process.TotalProcessorTime.TotalMilliseconds,
                    process.WorkingSet64 / 1024 / 1024,
                    hasWindow,
                    title);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return result;
    }
}
