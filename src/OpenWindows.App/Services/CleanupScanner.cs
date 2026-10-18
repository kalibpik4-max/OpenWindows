using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed class CleanupScanner
{
    private sealed record CleanTarget(
        string Title,
        string[] Paths,
        string Pattern,
        string Why,
        RiskLevel Risk,
        bool Checked,
        bool Admin,
        int MinMb);

    public List<Finding> GetFindings()
    {
        var findings = new List<Finding>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var targets = new List<CleanTarget>
        {
            new("User temp files",
                new[] { Path.Combine(localAppData, "Temp") },
                "*", "Leftover temp junk from installers and apps",
                RiskLevel.Safe, true, false, 10),
            new("Windows temp files",
                new[] { @"C:\Windows\Temp" },
                "*", "System temp files from installers and updates",
                RiskLevel.Safe, true, true, 10),
            new("Windows Update cache",
                new[] { @"C:\Windows\SoftwareDistribution\Download" },
                "*", "Downloaded update files - Windows re-downloads anything it still needs",
                RiskLevel.Safe, true, true, 50),
            new("Edge browser cache",
                new[]
                {
                    Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache")
                },
                "*", "Web cache - close Edge first; sites reload a little slower next visit",
                RiskLevel.Caution, false, false, 20),
            new("Chrome browser cache",
                new[]
                {
                    Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache")
                },
                "*", "Web cache - close Chrome first; sites reload a little slower next visit",
                RiskLevel.Caution, false, false, 20),
            new("Thumbnail cache",
                new[] { Path.Combine(localAppData, @"Microsoft\Windows\Explorer") },
                "thumbcache_*.db", "Explorer thumbnail database - rebuilds itself automatically",
                RiskLevel.Safe, true, false, 10)
        };

        foreach (var target in targets)
        {
            long size = 0;
            foreach (var path in target.Paths)
            {
                size += SystemInfoService.GetFolderSize(path, target.Pattern);
            }
            if (size < target.MinMb * 1024L * 1024L)
            {
                continue;
            }

            findings.Add(new Finding
            {
                Category = "Cleanup",
                Title = target.Title,
                Detail = $"{SystemInfoService.FormatSize(size)} reclaimable - {target.Why}",
                Risk = target.Risk,
                DefaultChecked = target.Checked,
                NeedsAdmin = target.Admin,
                Action = new CleanFolderAction(target.Paths, target.Pattern)
            });
        }

        var recycleBinSize = NativeMethods.GetRecycleBinSize();
        if (recycleBinSize > 50L * 1024L * 1024L)
        {
            findings.Add(new Finding
            {
                Category = "Cleanup",
                Title = "Empty the Recycle Bin",
                Detail = $"{SystemInfoService.FormatSize(recycleBinSize)} reclaimable - deleted files are still on disk until the bin is emptied. They will be gone permanently.",
                Risk = RiskLevel.Caution,
                DefaultChecked = false,
                NeedsAdmin = false,
                Action = new ClearRecycleBinAction()
            });
        }

        if (Directory.Exists(@"C:\Windows.old"))
        {
            var size = SystemInfoService.GetFolderSize(@"C:\Windows.old");
            findings.Add(new Finding
            {
                Category = "Cleanup",
                Title = "Remove previous Windows installation (Windows.old)",
                Detail = $"{SystemInfoService.FormatSize(size)} reclaimable - leftover files from a Windows upgrade. Applying opens Disk Cleanup so Windows removes it safely.",
                Risk = RiskLevel.Caution,
                DefaultChecked = false,
                NeedsAdmin = true,
                Action = new OpenDiskCleanupAction()
            });
        }

        return findings;
    }
}
