using Microsoft.Win32;

namespace OpenWindows.Services;

public static class AutostartService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenWindows";

    public static bool IsEnabled
    {
        get
        {
            var info = RegistryUtil.GetValueInfo("HKCU", RunKey, ValueName);
            return info.Exists;
        }
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (exe == null)
        {
            return;
        }
        RegistryUtil.SetValue("HKCU", RunKey, ValueName, $"\"{exe}\" --tray", RegistryValueKind.String);
    }

    public static void Disable()
    {
        RegistryUtil.RemoveValue("HKCU", RunKey, ValueName);
    }
}
