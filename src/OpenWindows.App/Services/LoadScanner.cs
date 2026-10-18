using System.Diagnostics;
using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed class LoadScanner
{
    private sealed record ProcessSample(string Name, double CpuMs, long WorkingSet);

    public List<Finding> GetFindings()
    {
        var findings = new List<Finding>();
        var cores = Environment.ProcessorCount;

        var first = Snapshot();
        Thread.Sleep(1000);
        var second = Snapshot();

        var rows = new List<(string Name, int Id, double Pct, long RamMb)>();
        foreach (var (pid, sample) in second)
        {
            if (!first.TryGetValue(pid, out var previous))
            {
                continue;
            }
            var delta = sample.CpuMs - previous.CpuMs;
            var pct = Math.Round(delta / (1000.0 * cores) * 100, 1);
            if (pct < 5)
            {
                continue;
            }
            if (ProtectedProcesses.IsProtected(sample.Name))
            {
                continue;
            }
            rows.Add((sample.Name, pid, pct, sample.WorkingSet / 1024 / 1024));
        }

        foreach (var row in rows.OrderByDescending(r => r.Pct).Take(6))
        {
            findings.Add(new Finding
            {
                Category = "Now",
                Title = $"High CPU right now: {row.Name}",
                Detail = $"Using about {row.Pct}% CPU ({row.RamMb} MB RAM). If you do not need it open, ending it frees CPU immediately.",
                Risk = RiskLevel.Caution,
                DefaultChecked = false,
                NeedsAdmin = false,
                Action = new KillProcessAction(row.Id, row.Name)
            });
        }

        try
        {
            var (usedPercent, _, _) = SystemInfoService.GetRamUsage();
            if (usedPercent >= 85)
            {
                var topRam = Process.GetProcesses()
                    .Where(p => !ProtectedProcesses.IsProtected(p.ProcessName))
                    .OrderByDescending(p => p.WorkingSet64)
                    .FirstOrDefault();

                if (topRam != null)
                {
                    findings.Add(new Finding
                    {
                        Category = "Now",
                        Title = $"Memory hog: {topRam.ProcessName}",
                        Detail = $"Memory is {(int)usedPercent}% full and this process holds {topRam.WorkingSet64 / 1024 / 1024} MB. Consider closing it if you are not using it.",
                        Risk = RiskLevel.Caution,
                        DefaultChecked = false,
                        NeedsAdmin = false,
                        Action = new KillProcessAction(topRam.Id, topRam.ProcessName)
                    });
                }
            }
        }
        catch
        {
        }

        return findings;
    }

    private static Dictionary<int, ProcessSample> Snapshot()
    {
        var result = new Dictionary<int, ProcessSample>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                result[process.Id] = new ProcessSample(
                    process.ProcessName,
                    process.TotalProcessorTime.TotalMilliseconds,
                    process.WorkingSet64);
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
