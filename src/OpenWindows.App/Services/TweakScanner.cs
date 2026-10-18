using System.Text.RegularExpressions;
using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed class TweakScanner
{
    private static readonly Regex GuidPattern = new("([0-9a-fA-F-]{36})", RegexOptions.Compiled);
    private static readonly Regex NamePattern = new(@"\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex FastPlanPattern = new("High performance|Ultimate Performance", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public List<Finding> GetFindings()
    {
        var findings = new List<Finding>();

        try
        {
            var (exitCode, output, _) = ProcessRunner.Run("powercfg", "/getactivescheme");
            if (exitCode == 0)
            {
                var guidMatch = GuidPattern.Match(output);
                var nameMatch = NamePattern.Match(output);
                if (guidMatch.Success && nameMatch.Success)
                {
                    var planName = nameMatch.Groups[1].Value;
                    if (!FastPlanPattern.IsMatch(planName))
                    {
                        findings.Add(new Finding
                        {
                            Category = "Tweaks",
                            Title = "Use the High performance power plan",
                            Detail = $"Currently \"{planName}\". High performance keeps CPU clocks up for snappier response; it uses a bit more battery. Undo restores your old plan.",
                            Risk = RiskLevel.Caution,
                            DefaultChecked = true,
                            NeedsAdmin = false,
                            Action = new PowerPlanAction(Catalogs.HighPerformancePlanGuid)
                        });
                    }
                }
            }
        }
        catch
        {
        }

        var vfx = RegistryUtil.GetValueInfo("HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
        var vfxValue = -1;
        if (vfx.Exists)
        {
            vfxValue = TryToInt(vfx.Value, -1);
        }
        if (vfxValue != 2)
        {
            findings.Add(new Finding
            {
                Category = "Tweaks",
                Title = "Set visual effects for best performance",
                Detail = "Turns off animations, shadows, and other eye candy. Applies after you sign out and back in. Revert any time in Windows Settings, or with Undo.",
                Risk = RiskLevel.Caution,
                DefaultChecked = true,
                NeedsAdmin = false,
                Action = new RegSetAction("HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                    "VisualFXSetting", 2, "DWord")
            });
        }

        var transparency = RegistryUtil.GetValueInfo("HKCU",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");
        var transparencyValue = -1;
        if (transparency.Exists)
        {
            transparencyValue = TryToInt(transparency.Value, -1);
        }
        if (transparencyValue != 0)
        {
            findings.Add(new Finding
            {
                Category = "Tweaks",
                Title = "Disable window transparency",
                Detail = "Removes the blur behind menus and windows - a small but free GPU/CPU saving on weak hardware.",
                Risk = RiskLevel.Safe,
                DefaultChecked = true,
                NeedsAdmin = false,
                Action = new RegSetAction("HKCU",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "EnableTransparency", 0, "DWord")
            });
        }

        var telemetry = RegistryUtil.GetValueInfo("HKLM",
            @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
        var telemetryValue = -1;
        if (telemetry.Exists)
        {
            telemetryValue = TryToInt(telemetry.Value, -1);
        }
        if (telemetryValue != 0)
        {
            findings.Add(new Finding
            {
                Category = "Tweaks",
                Title = "Set diagnostic data to minimum (telemetry off)",
                Detail = "Tells Windows to stop collecting and uploading diagnostic data. Needs admin rights.",
                Risk = RiskLevel.Safe,
                DefaultChecked = true,
                NeedsAdmin = true,
                Action = new RegSetAction("HKLM",
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    "AllowTelemetry", 0, "DWord")
            });
        }

        return findings;
    }

    private static int TryToInt(object? value, int fallback)
    {
        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return fallback;
        }
    }
}
