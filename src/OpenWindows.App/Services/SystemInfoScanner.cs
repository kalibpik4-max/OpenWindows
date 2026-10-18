using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed class SystemInfoScanner
{
    public List<Finding> GetFindings()
    {
        var findings = new List<Finding>();
        try
        {
            var specs = SystemInfoService.GetSpecs();
            var (ramUsedPercent, ramUsedGb, ramTotalGb) = SystemInfoService.GetRamUsage();
            var (_, diskFreeGb, diskTotalGb) = SystemInfoService.GetDiskUsage("C");

            findings.Add(new Finding
            {
                Category = "System",
                Title = $"CPU: {specs.CpuName}",
                Detail = $"{specs.Cores} cores / {specs.Threads} threads, max {specs.MaxClockMhz} MHz - this is the part that limits everything else",
                Risk = RiskLevel.Info,
                DefaultChecked = false,
                NeedsAdmin = false,
                Action = null
            });

            findings.Add(new Finding
            {
                Category = "System",
                Title = $"Memory: {ramUsedGb:N1} GB used of {ramTotalGb:N1} GB ({(int)ramUsedPercent}%)",
                Detail = "Snappy with 16 GB; focus on CPU hogs and disk space instead",
                Risk = RiskLevel.Info,
                DefaultChecked = false,
                NeedsAdmin = false,
                Action = null
            });

            var diskFreePercent = diskTotalGb > 0 ? (int)(diskFreeGb / diskTotalGb * 100) : 0;
            findings.Add(new Finding
            {
                Category = "System",
                Title = $"Disk C: {diskFreeGb:N1} GB free of {diskTotalGb:N0} GB ({diskFreePercent}%)",
                Detail = "NVMe SSD - keep at least 20% free so it keeps its speed",
                Risk = RiskLevel.Info,
                DefaultChecked = false,
                NeedsAdmin = false,
                Action = null
            });

            if (diskFreePercent < 20)
            {
                findings.Add(new Finding
                {
                    Category = "System",
                    Title = "Low free disk space",
                    Detail = $"Only {diskFreePercent}% free on C:. SSDs slow down noticeably when nearly full - review the cleanup items further down.",
                    Risk = RiskLevel.Caution,
                    DefaultChecked = false,
                    NeedsAdmin = false,
                    Action = null
                });
            }
        }
        catch (Exception ex)
        {
            Log.Write("System info scan failed: " + ex.Message);
        }

        return findings;
    }
}
