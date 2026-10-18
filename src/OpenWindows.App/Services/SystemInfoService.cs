using Microsoft.Win32;

namespace OpenWindows.Services;

public sealed record SystemSpecs(
    string CpuName,
    int Cores,
    int Threads,
    int MaxClockMhz,
    double RamTotalGb,
    string OsName,
    string OsVersion,
    string OsBuild,
    int ProcessorCount);

public static class SystemInfoService
{
    public static SystemSpecs GetSpecs()
    {
        var cpuName = "Unknown CPU";
        var maxMhz = 0;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpuName = key?.GetValue("ProcessorNameString") as string ?? cpuName;
            maxMhz = key?.GetValue("~MHz") is int mhz ? mhz : 0;
        }
        catch
        {
        }

        var ramTotal = NativeMethods.GetTotalPhysicalMemory();
        var osName = "Windows";
        var osVersion = "";
        var osBuild = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            osName = key?.GetValue("ProductName") as string ?? osName;
            osVersion = key?.GetValue("DisplayVersion") as string ?? "";
            var build = key?.GetValue("CurrentBuild") as string;
            var ubr = key?.GetValue("UBR") is int u ? u : 0;
            if (build != null)
            {
                osBuild = $"{build}.{ubr}";
            }
        }
        catch
        {
        }

        return new SystemSpecs(
            cpuName.Trim(),
            Environment.ProcessorCount / 2 > 0 ? Math.Max(1, Environment.ProcessorCount / 2) : 1,
            Environment.ProcessorCount,
            maxMhz,
            ramTotal / 1024.0 / 1024.0 / 1024.0,
            osName,
            osVersion,
            osBuild,
            Environment.ProcessorCount);
    }

    public static (double UsedPercent, double UsedGb, double TotalGb) GetRamUsage()
    {
        var total = NativeMethods.GetTotalPhysicalMemory();
        var free = NativeMethods.GetAvailablePhysicalMemory();
        if (total == 0)
        {
            return (0, 0, 0);
        }
        var used = total - free;
        return (used * 100.0 / total, used / 1024.0 / 1024.0 / 1024.0, total / 1024.0 / 1024.0 / 1024.0);
    }

    public static (double UsedPercent, double FreeGb, double TotalGb) GetDiskUsage(string drive = "C")
    {
        try
        {
            var info = new DriveInfo(drive);
            var total = info.TotalSize;
            var free = info.TotalFreeSpace;
            if (total == 0)
            {
                return (0, 0, 0);
            }
            return ((total - free) * 100.0 / total, free / 1024.0 / 1024.0 / 1024.0, total / 1024.0 / 1024.0 / 1024.0);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1073741824)
        {
            return $"{bytes / 1073741824.0:N2} GB";
        }
        if (bytes >= 1048576)
        {
            return $"{bytes / 1048576.0:N0} MB";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:N0} KB";
        }
        return $"{bytes} B";
    }

    public static long GetFolderSize(string path, string pattern = "*")
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }
        try
        {
            long sum = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.None,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            foreach (var file in Directory.EnumerateFiles(path, pattern, options))
            {
                try
                {
                    sum += new FileInfo(file).Length;
                }
                catch
                {
                }
            }
            return sum;
        }
        catch
        {
            return 0;
        }
    }
}
