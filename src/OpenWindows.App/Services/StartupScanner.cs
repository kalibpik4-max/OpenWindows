using System.Text.RegularExpressions;
using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed class StartupScanner
{
    private static readonly Regex Protect = new(
        Catalogs.StartupProtectPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public List<Finding> GetFindings()
    {
        var findings = new List<Finding>();

        var locations = new (string Hive, string SubKey, string Label)[]
        {
            ("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU Run key"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run key"),
            ("HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run key (32-bit)")
        };

        foreach (var (hive, subKey, label) in locations)
        {
            foreach (var pair in RegistryUtil.EnumerateValues(hive, subKey))
            {
                if (pair.Key.StartsWith("PS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var command = pair.Value?.ToString() ?? "";
                var commandShort = command.Length > 110 ? command[..107] + "..." : command;
                var risky = Protect.IsMatch(pair.Key) || Protect.IsMatch(command);
                var needsAdmin = hive == "HKLM";

                findings.Add(new Finding
                {
                    Category = "Startup",
                    Title = $"Startup: {pair.Key}",
                    Detail = $"Starts itself at sign-in ({label}). Disabling frees boot time and RAM. Command: {commandShort}",
                    Risk = risky ? RiskLevel.Caution : RiskLevel.Safe,
                    DefaultChecked = !risky,
                    NeedsAdmin = needsAdmin,
                    Action = new RegRemoveAction(hive, subKey, pair.Key)
                });
            }
        }

        var folders = new (string Path, string Label, bool Admin)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), "your Startup folder", false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "the common Startup folder", true)
        };

        foreach (var (folderPath, label, admin) in folders)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folderPath);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var risky = Protect.IsMatch(fileName);
                findings.Add(new Finding
                {
                    Category = "Startup",
                    Title = $"Startup: {Path.GetFileNameWithoutExtension(fileName)}",
                    Detail = $"Shortcut in {label} - runs at sign-in. Disabling moves the shortcut to a safe backup folder.",
                    Risk = risky ? RiskLevel.Caution : RiskLevel.Safe,
                    DefaultChecked = !risky,
                    NeedsAdmin = admin,
                    Action = new MoveFileAction(file)
                });
            }
        }

        return findings;
    }
}
