namespace OpenWindows.Services;

public static class ProtectedProcesses
{
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system",
        "csrss", "wininit", "winlogon", "services", "lsass", "smss", "dwm",
        "explorer", "svchost", "fontdrvhost", "sihost", "ctfmon", "taskhostw",
        "audiodg", "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray",
        "widgets", "widgetservice", "shellexperiencehost", "startmenuexperiencehost",
        "searchhost", "searchindexer", "textinputhost", "powershell", "pwsh", "conhost",
        "windowsterminal", "applicationframehost", "runtimebroker", "openwindows", "opencode",
        "sgrmbroker", "lsaiso", "wmiprvse", "dllhost", "spoolsv", "taskmgr"
    };

    public static bool IsProtected(string processName) => Names.Contains(processName);
}
