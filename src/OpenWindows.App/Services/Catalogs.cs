using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed record ServiceCatalogItem(string Name, string Target, RiskLevel Risk, string Why);

public sealed record TaskCatalogItem(string Path, string Name, string Why);

public static class Catalogs
{
    public static readonly IReadOnlyList<ServiceCatalogItem> Services = new List<ServiceCatalogItem>
    {
        new("DiagTrack", "Disabled", RiskLevel.Safe, "Windows telemetry - collects and uploads diagnostic data in the background"),
        new("dmwappushservice", "Disabled", RiskLevel.Safe, "WAP push message routing - not used on a typical PC"),
        new("MapsBroker", "Disabled", RiskLevel.Safe, "Downloaded Maps manager - only used by the Maps app"),
        new("RetailDemo", "Disabled", RiskLevel.Safe, "Retail demo mode - not needed on a personal PC"),
        new("WMPNetworkSvc", "Disabled", RiskLevel.Safe, "Windows Media Player network sharing - almost never used"),
        new("RemoteRegistry", "Disabled", RiskLevel.Safe, "Allows remote registry access - a security risk and unused on home PCs"),
        new("Fax", "Disabled", RiskLevel.Safe, "Fax support - almost never needed"),
        new("SysMain", "Manual", RiskLevel.Caution, "Superfetch preloads apps into memory - on a slower CPU it causes background churn; safe to set to Manual"),
        new("WSearch", "Manual", RiskLevel.Caution, "Windows Search indexing - heavy on disk and CPU if you rarely search files"),
        new("Spooler", "Disabled", RiskLevel.Caution, "Print Spooler - disable only if you never print"),
        new("XblAuthManager", "Disabled", RiskLevel.Caution, "Xbox Live sign-in - needed by Xbox and Game Pass games"),
        new("XblGameSave", "Disabled", RiskLevel.Caution, "Xbox cloud saves - needed by Xbox and Game Pass games"),
        new("XboxNetApiSvc", "Disabled", RiskLevel.Caution, "Xbox networking - needed by Xbox and Game Pass games"),
        new("XboxGipSvc", "Disabled", RiskLevel.Caution, "Xbox accessory management - needed for Xbox controllers")
    };

    public static readonly IReadOnlyList<TaskCatalogItem> Tasks = new List<TaskCatalogItem>
    {
        new(@"\Microsoft\Windows\Application Experience\", "Microsoft Compatibility Appraiser", "Telemetry task that scans installed programs shortly after boot"),
        new(@"\Microsoft\Windows\Application Experience\", "ProgramDataUpdater", "Telemetry task for the Application Experience program"),
        new(@"\Microsoft\Windows\Customer Experience Improvement Program\", "Consolidator", "Uploads Windows usage data to Microsoft"),
        new(@"\Microsoft\Windows\Feedback\Siuf\", "DmClient", "Feedback and telemetry client that runs periodically"),
        new(@"\Microsoft\Windows\Feedback\Siuf\", "DmClientOnScenarioDownload", "Feedback and telemetry client for scenario data"),
        new(@"\Microsoft\Windows\Autochk\", "Proxy", "Telemetry proxy task used on systems that cannot send data directly")
    };

    public const string HighPerformancePlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public const string StartupProtectPattern =
        "security|defender|antivirus|virus|audio|realtek|intel|amd|nvidia|graphics|synaptics|elan|touchpad|trackpad|camera|webcam|waves|dolby|wireless|wifi|bluetooth|dell|lenovo|hp |asus|acer|msi|toshiba|logi|razer|corsair|steelseries|onedrive|dropbox|icloud|google drive|megasync|keepass|bitwarden|lastpass|1password|vpn|backup|acronis|macrium|teamviewer|anydesk|logmein";
}
