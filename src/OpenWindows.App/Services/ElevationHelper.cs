using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace OpenWindows.Services;

public static class ElevationHelper
{
    public static bool IsAdmin { get; } = CheckAdmin();

    private static bool CheckAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RelaunchElevated(string extraArgs = "")
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null)
            {
                return false;
            }
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = extraArgs,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
