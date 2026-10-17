param(
    [switch]$Console,
    [switch]$UiSmokeTest,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Continue'

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsSta {
    return [Threading.Thread]::CurrentThread.GetApartmentState() -eq [Threading.ApartmentState]::STA
}

if ((-not $Console) -and (-not $UiSmokeTest) -and (-not $SelfTest) -and (-not (Test-IsSta)) -and (-not $env:WINOPT_RELAUNCH)) {
    $env:WINOPT_RELAUNCH = '1'
    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    exit
}

$script:IsAdmin = Test-IsAdmin
$script:AppDir = Join-Path $env:LOCALAPPDATA 'WinOptimizer'
$script:BackupDir = Join-Path $script:AppDir 'backups'
$script:LogFile = Join-Path $script:AppDir 'optimizer.log'
$script:SessionUndo = New-Object System.Collections.ArrayList
$script:Notify = $null
$script:MonTimer = $null
$script:MonState = @{ Running = $false; Snap = @{}; LastTick = $null; Streak = 0; LastAlert = $null }
$script:UI = $null

$script:ProtectedProcesses = @(
    'system', 'idle', 'registry', 'memory compression', 'secure system',
    'csrss', 'wininit', 'winlogon', 'services', 'lsass', 'smss', 'dwm',
    'explorer', 'svchost', 'fontdrvhost', 'sihost', 'ctfmon', 'taskhostw',
    'audiodg', 'msmpeng', 'nissrv', 'securityhealthservice', 'securityhealthsystray',
    'widgets', 'widgetservice', 'shellexperiencehost', 'startmenuexperiencehost',
    'searchhost', 'searchindexer', 'textinputhost', 'powershell', 'pwsh', 'conhost',
    'windowsterminal', 'applicationframehost', 'runtimebroker', 'winoptimizer',
    'sgrmbroker', 'lsaiso', 'wmiprvse', 'dllhost', 'spoolsv', 'taskmgr'
)

$script:ServiceCatalog = @(
    @{ Name = 'DiagTrack'; Target = 'Disabled'; Risk = 'Safe'; Why = 'Windows telemetry - collects and uploads diagnostic data in the background' },
    @{ Name = 'dmwappushservice'; Target = 'Disabled'; Risk = 'Safe'; Why = 'WAP push message routing - not used on a typical PC' },
    @{ Name = 'MapsBroker'; Target = 'Disabled'; Risk = 'Safe'; Why = 'Downloaded Maps manager - only used by the Maps app' },
    @{ Name = 'RetailDemo'; Target = 'Disabled'; Risk = 'Safe'; Why = 'Retail demo mode - not needed on a personal PC' },
    @{ Name = 'WMPNetworkSvc'; Target = 'Disabled'; Risk = 'Safe'; Why = 'Windows Media Player network sharing - almost never used' },
    @{ Name = 'RemoteRegistry'; Target = 'Disabled'; Risk = 'Safe'; Why = 'Allows remote registry access - a security risk and unused on home PCs' },
    @{ Name = 'Fax'; Target = 'Disabled'; Risk = 'Safe'; Why = 'Fax support - almost never needed' },
    @{ Name = 'SysMain'; Target = 'Manual'; Risk = 'Caution'; Why = 'Superfetch preloads apps into memory - on a slower CPU it causes background churn; safe to set to Manual' },
    @{ Name = 'WSearch'; Target = 'Manual'; Risk = 'Caution'; Why = 'Windows Search indexing - heavy on disk and CPU if you rarely search files' },
    @{ Name = 'Spooler'; Target = 'Disabled'; Risk = 'Caution'; Why = 'Print Spooler - disable only if you never print' },
    @{ Name = 'XblAuthManager'; Target = 'Disabled'; Risk = 'Caution'; Why = 'Xbox Live sign-in - needed by Xbox and Game Pass games' },
    @{ Name = 'XblGameSave'; Target = 'Disabled'; Risk = 'Caution'; Why = 'Xbox cloud saves - needed by Xbox and Game Pass games' },
    @{ Name = 'XboxNetApiSvc'; Target = 'Disabled'; Risk = 'Caution'; Why = 'Xbox networking - needed by Xbox and Game Pass games' },
    @{ Name = 'XboxGipSvc'; Target = 'Disabled'; Risk = 'Caution'; Why = 'Xbox accessory management - needed for Xbox controllers' }
)

$script:TaskCatalog = @(
    @{ Path = '\Microsoft\Windows\Application Experience\'; Name = 'Microsoft Compatibility Appraiser'; Why = 'Telemetry task that scans installed programs shortly after boot' },
    @{ Path = '\Microsoft\Windows\Application Experience\'; Name = 'ProgramDataUpdater'; Why = 'Telemetry task for the Application Experience program' },
    @{ Path = '\Microsoft\Windows\Customer Experience Improvement Program\'; Name = 'Consolidator'; Why = 'Uploads Windows usage data to Microsoft' },
    @{ Path = '\Microsoft\Windows\Feedback\Siuf\'; Name = 'DmClient'; Why = 'Feedback and telemetry client that runs periodically' },
    @{ Path = '\Microsoft\Windows\Feedback\Siuf\'; Name = 'DmClientOnScenarioDownload'; Why = 'Feedback and telemetry client for scenario data' },
    @{ Path = '\Microsoft\Windows\Autochk\'; Name = 'Proxy'; Why = 'Telemetry proxy task used on systems that cannot send data directly' }
)

function Write-Log {
    param([string]$Message)
    try {
        if (-not (Test-Path -LiteralPath $script:AppDir)) { New-Item -ItemType Directory -Path $script:AppDir -Force | Out-Null }
        Add-Content -LiteralPath $script:LogFile -Value ('[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message) -ErrorAction SilentlyContinue
    } catch { }
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1073741824) { return ('{0:N2} GB' -f ($Bytes / 1073741824)) }
    if ($Bytes -ge 1048576) { return ('{0:N0} MB' -f ($Bytes / 1048576)) }
    if ($Bytes -ge 1024) { return ('{0:N0} KB' -f ($Bytes / 1024)) }
    return ('{0} B' -f $Bytes)
}

function Get-FolderSize {
    param([string]$Path, [string]$Pattern = '*')
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    try {
        $sum = (Get-ChildItem -LiteralPath $Path -Force -Recurse -Filter $Pattern -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum -ErrorAction SilentlyContinue).Sum
        if ($null -eq $sum) { return 0 }
        return [long]$sum
    } catch { return 0 }
}

function Get-RegValueInfo {
    param([string]$Hive, [string]$SubKey, [string]$Name)
    $path = '{0}:\{1}' -f $Hive, $SubKey
    if (-not (Test-Path -LiteralPath $path)) { return [pscustomobject]@{ Exists = $false; Value = $null; Kind = $null } }
    try {
        $item = Get-Item -LiteralPath $path -ErrorAction Stop
        $kind = $item.GetValueKind($Name)
        $val = $item.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        return [pscustomobject]@{ Exists = $true; Value = $val; Kind = $kind.ToString() }
    } catch {
        return [pscustomobject]@{ Exists = $false; Value = $null; Kind = $null }
    }
}

function Set-RegValue {
    param([string]$Hive, [string]$SubKey, [string]$Name, $Value, [string]$Kind = 'String', [switch]$Track)
    $path = '{0}:\{1}' -f $Hive, $SubKey
    if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }
    $old = $null
    if ($Track) { $old = Get-RegValueInfo $Hive $SubKey $Name }
    $type = 'String'
    if ($Kind -in @('String', 'ExpandString', 'DWord', 'QWord', 'MultiString')) { $type = $Kind }
    Set-ItemProperty -LiteralPath $path -Name $Name -Value $Value -Type $type -ErrorAction Stop
    if ($Track) {
        [void]$script:SessionUndo.Add([pscustomobject]@{ Type = 'RegSet'; Hive = $Hive; SubKey = $SubKey; Name = $Name; OldExists = [bool]$old.Exists; OldValue = $old.Value; OldKind = $old.Kind })
    }
}

function Remove-RegValueTracked {
    param([string]$Hive, [string]$SubKey, [string]$Name, [switch]$Track)
    $path = '{0}:\{1}' -f $Hive, $SubKey
    $old = Get-RegValueInfo $Hive $SubKey $Name
    if (-not $old.Exists) { return $false }
    Remove-ItemProperty -LiteralPath $path -Name $Name -Force -ErrorAction Stop
    if ($Track) {
        [void]$script:SessionUndo.Add([pscustomobject]@{ Type = 'RegRemoved'; Hive = $Hive; SubKey = $SubKey; Name = $Name; OldValue = $old.Value; OldKind = $old.Kind })
    }
    return $true
}

function Get-SystemInfoFindings {
    $out = New-Object System.Collections.ArrayList
    try {
        $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $d = Get-PSDrive C
        $usedRamMB = ($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1KB
        $totalRamMB = $os.TotalVisibleMemorySize / 1KB
        $ramPct = 0
        if ($totalRamMB -gt 0) { $ramPct = [int](($usedRamMB / $totalRamMB) * 100) }
        $totalDisk = $d.Used + $d.Free
        $freePct = 0
        if ($totalDisk -gt 0) { $freePct = [int](($d.Free / $totalDisk) * 100) }
        $null = $out.Add([pscustomobject]@{ Category = 'System'; Title = ('CPU: {0}' -f $cpu.Name.Trim()); Detail = ('{0} cores / {1} threads, max {2} MHz - this is the part that limits everything else' -f $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors, $cpu.MaxClockSpeed); Risk = 'Info'; DefaultChecked = $false; NeedsAdmin = $false; Action = $null })
        $null = $out.Add([pscustomobject]@{ Category = 'System'; Title = ('Memory: {0:N1} GB used of {1:N1} GB ({2}%)' -f ($usedRamMB / 1024), ($totalRamMB / 1024), $ramPct); Detail = 'Snappy with 16 GB; focus on CPU hogs and disk space instead'; Risk = 'Info'; DefaultChecked = $false; NeedsAdmin = $false; Action = $null })
        $null = $out.Add([pscustomobject]@{ Category = 'System'; Title = ('Disk C: {0:N1} GB free of {1:N0} GB ({2}%)' -f ($d.Free / 1GB), ($totalDisk / 1GB), $freePct); Detail = 'NVMe SSD - keep at least 20% free so it keeps its speed'; Risk = 'Info'; DefaultChecked = $false; NeedsAdmin = $false; Action = $null })
        if ($freePct -lt 20) {
            $null = $out.Add([pscustomobject]@{ Category = 'System'; Title = 'Low free disk space'; Detail = ('Only {0}% free on C:. SSDs slow down noticeably when nearly full - review the cleanup items further down.' -f $freePct); Risk = 'Caution'; DefaultChecked = $false; NeedsAdmin = $false; Action = $null })
        }
    } catch { }
    return $out
}

function Get-StartupFindings {
    $out = New-Object System.Collections.ArrayList
    $protect = 'security|defender|antivirus|virus|audio|realtek|intel|amd|nvidia|graphics|synaptics|elan|touchpad|trackpad|camera|webcam|waves|dolby|wireless|wifi|bluetooth|dell|lenovo|hp |asus|acer|msi|toshiba|logi|razer|corsair|steelseries|onedrive|dropbox|icloud|google drive|megasync|keepass|bitwarden|lastpass|1password|vpn|backup|acronis|macrium|teamviewer|anydesk|logmein'
    $locations = @(
        @{ Hive = 'HKCU'; SubKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run'; Label = 'HKCU Run key' },
        @{ Hive = 'HKLM'; SubKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run'; Label = 'HKLM Run key' },
        @{ Hive = 'HKLM'; SubKey = 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; Label = 'HKLM Run key (32-bit)' }
    )
    foreach ($loc in $locations) {
        $path = '{0}:\{1}' -f $loc.Hive, $loc.SubKey
        if (-not (Test-Path -LiteralPath $path)) { continue }
        try { $props = Get-ItemProperty -LiteralPath $path -ErrorAction Stop } catch { continue }
        if ($null -eq $props) { continue }
        foreach ($p in $props.PSObject.Properties) {
            if ($p.Name -like 'PS*') { continue }
            $cmd = [string]$p.Value
            $cmdShort = $cmd
            if ($cmdShort.Length -gt 110) { $cmdShort = $cmdShort.Substring(0, 107) + '...' }
            $risky = ($p.Name -match $protect) -or ($cmd -match $protect)
            $risk = 'Safe'
            $checked = $true
            if ($risky) { $risk = 'Caution'; $checked = $false }
            $null = $out.Add([pscustomobject]@{
                Category = 'Startup'
                Title = ('Startup: {0}' -f $p.Name)
                Detail = ('Starts itself at sign-in ({0}). Disabling frees boot time and RAM. Command: {1}' -f $loc.Label, $cmdShort)
                Risk = $risk; DefaultChecked = $checked; NeedsAdmin = ($loc.Hive -eq 'HKLM')
                Action = @{ Type = 'RegRemoved'; Hive = $loc.Hive; SubKey = $loc.SubKey; Name = $p.Name }
            })
        }
    }
    $folders = @(
        @{ Path = [Environment]::GetFolderPath('Startup'); Label = 'your Startup folder'; Admin = $false },
        @{ Path = [Environment]::GetFolderPath('CommonStartup'); Label = 'the common Startup folder'; Admin = $true }
    )
    foreach ($fi in $folders) {
        $folder = $fi.Path
        if (-not $folder) { continue }
        if (-not (Test-Path -LiteralPath $folder)) { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $folder -File -ErrorAction SilentlyContinue)) {
            $risky = ($f.Name -match $protect)
            $risk = 'Safe'
            $checked = $true
            if ($risky) { $risk = 'Caution'; $checked = $false }
            $null = $out.Add([pscustomobject]@{
                Category = 'Startup'
                Title = ('Startup: {0}' -f ([IO.Path]::GetFileNameWithoutExtension($f.Name)))
                Detail = ('Shortcut in {0} - runs at sign-in. Disabling moves the shortcut to a safe backup folder.' -f $fi.Label)
                Risk = $risk; DefaultChecked = $checked; NeedsAdmin = $fi.Admin
                Action = @{ Type = 'FileMoved'; Source = $f.FullName }
            })
        }
    }
    return $out
}

function Get-ServiceFindings {
    $out = New-Object System.Collections.ArrayList
    try { $svcList = @(Get-CimInstance Win32_Service -ErrorAction Stop) } catch { return $out }
    $map = @{}
    foreach ($s in $svcList) { $map[$s.Name] = $s }
    foreach ($c in $script:ServiceCatalog) {
        if (-not $map.ContainsKey($c.Name)) { continue }
        $s = $map[$c.Name]
        if ($s.StartMode -eq $c.Target) { continue }
        $null = $out.Add([pscustomobject]@{
            Category = 'Service'
            Title = ('Service: {0}' -f $c.Name)
            Detail = ('{0}. Currently {1} ({2}).' -f $c.Why, $s.StartMode, $s.State)
            Risk = $c.Risk; DefaultChecked = ($c.Risk -eq 'Safe'); NeedsAdmin = $true
            Action = @{ Type = 'Service'; Name = $c.Name; Target = $c.Target; Stop = $true }
        })
    }
    return $out
}

function Get-TaskFindings {
    $out = New-Object System.Collections.ArrayList
    foreach ($t in $script:TaskCatalog) {
        try { $task = Get-ScheduledTask -TaskPath $t.Path -TaskName $t.Name -ErrorAction Stop } catch { continue }
        if ($task.State -eq 'Disabled') { continue }
        $null = $out.Add([pscustomobject]@{
            Category = 'Task'
            Title = ('Task: {0}' -f $t.Name)
            Detail = ('{0}. Disabling removes a small periodic background load.' -f $t.Why)
            Risk = 'Safe'; DefaultChecked = $true; NeedsAdmin = $true
            Action = @{ Type = 'ScheduledTask'; Path = $t.Path; Name = $t.Name }
        })
    }
    return $out
}

function Get-TweakFindings {
    $out = New-Object System.Collections.ArrayList
    try {
        $schemeOut = (powercfg /getactivescheme) -join ' '
        $guidMatch = [regex]::Match($schemeOut, '([0-9a-fA-F-]{36})')
        $nameMatch = [regex]::Match($schemeOut, '\(([^)]+)\)')
        if ($guidMatch.Success -and $nameMatch.Success) {
            $planName = $nameMatch.Groups[1].Value
            if ($planName -notmatch 'High performance|Ultimate Performance') {
                $null = $out.Add([pscustomobject]@{
                    Category = 'Tweaks'
                    Title = 'Use the High performance power plan'
                    Detail = ('Currently "{0}". High performance keeps CPU clocks up for snappier response; it uses a bit more battery. Undo restores your old plan.' -f $planName)
                    Risk = 'Caution'; DefaultChecked = $true; NeedsAdmin = $false
                    Action = @{ Type = 'PowerPlan'; Guid = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' }
                })
            }
        }
    } catch { }
    $vfx = Get-RegValueInfo 'HKCU' 'Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting'
    $vfxVal = -1
    if ($vfx.Exists) { try { $vfxVal = [int]$vfx.Value } catch { $vfxVal = -1 } }
    if ($vfxVal -ne 2) {
        $null = $out.Add([pscustomobject]@{
            Category = 'Tweaks'
            Title = 'Set visual effects for best performance'
            Detail = 'Turns off animations, shadows, and other eye candy. Applies after you sign out and back in. Revert any time in Windows Settings, or with Undo.'
            Risk = 'Caution'; DefaultChecked = $true; NeedsAdmin = $false
            Action = @{ Type = 'RegSet'; Hive = 'HKCU'; SubKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'; Name = 'VisualFXSetting'; Value = 2; Kind = 'DWord' }
        })
    }
    $tr = Get-RegValueInfo 'HKCU' 'SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'EnableTransparency'
    $trVal = -1
    if ($tr.Exists) { try { $trVal = [int]$tr.Value } catch { $trVal = -1 } }
    if ($trVal -ne 0) {
        $null = $out.Add([pscustomobject]@{
            Category = 'Tweaks'
            Title = 'Disable window transparency'
            Detail = 'Removes the blur behind menus and windows - a small but free GPU/CPU saving on weak hardware.'
            Risk = 'Safe'; DefaultChecked = $true; NeedsAdmin = $false
            Action = @{ Type = 'RegSet'; Hive = 'HKCU'; SubKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Name = 'EnableTransparency'; Value = 0; Kind = 'DWord' }
        })
    }
    $tel = Get-RegValueInfo 'HKLM' 'SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry'
    $telVal = -1
    if ($tel.Exists) { try { $telVal = [int]$tel.Value } catch { $telVal = -1 } }
    if ($telVal -ne 0) {
        $null = $out.Add([pscustomobject]@{
            Category = 'Tweaks'
            Title = 'Set diagnostic data to minimum (telemetry off)'
            Detail = 'Tells Windows to stop collecting and uploading diagnostic data. Needs admin rights.'
            Risk = 'Safe'; DefaultChecked = $true; NeedsAdmin = $true
            Action = @{ Type = 'RegSet'; Hive = 'HKLM'; SubKey = 'SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry'; Value = 0; Kind = 'DWord' }
        })
    }
    return $out
}

function Get-CleanupFindings {
    $out = New-Object System.Collections.ArrayList
    $targets = @(
        @{ Title = 'User temp files'; Paths = @((Join-Path $env:LOCALAPPDATA 'Temp')); Pattern = '*'; Why = 'Leftover temp junk from installers and apps'; Risk = 'Safe'; Checked = $true; Admin = $false; MinMB = 10 },
        @{ Title = 'Windows temp files'; Paths = @('C:\Windows\Temp'); Pattern = '*'; Why = 'System temp files from installers and updates'; Risk = 'Safe'; Checked = $true; Admin = $true; MinMB = 10 },
        @{ Title = 'Windows Update cache'; Paths = @('C:\Windows\SoftwareDistribution\Download'); Pattern = '*'; Why = 'Downloaded update files - Windows re-downloads anything it still needs'; Risk = 'Safe'; Checked = $true; Admin = $true; MinMB = 50 },
        @{ Title = 'Edge browser cache'; Paths = @((Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data\Default\Cache'), (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data\Default\Code Cache')); Pattern = '*'; Why = 'Web cache - close Edge first; sites reload a little slower next visit'; Risk = 'Caution'; Checked = $false; Admin = $false; MinMB = 20 },
        @{ Title = 'Chrome browser cache'; Paths = @((Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data\Default\Cache'), (Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data\Default\Code Cache')); Pattern = '*'; Why = 'Web cache - close Chrome first; sites reload a little slower next visit'; Risk = 'Caution'; Checked = $false; Admin = $false; MinMB = 20 },
        @{ Title = 'Thumbnail cache'; Paths = @((Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer')); Pattern = 'thumbcache_*.db'; Why = 'Explorer thumbnail database - rebuilds itself automatically'; Risk = 'Safe'; Checked = $true; Admin = $false; MinMB = 10 }
    )
    foreach ($t in $targets) {
        $size = 0
        foreach ($p in $t.Paths) { $size += Get-FolderSize -Path $p -Pattern $t.Pattern }
        if ($size -lt ($t.MinMB * 1MB)) { continue }
        $null = $out.Add([pscustomobject]@{
            Category = 'Cleanup'
            Title = $t.Title
            Detail = ('{0} reclaimable - {1}' -f (Format-Size $size), $t.Why)
            Risk = $t.Risk; DefaultChecked = $t.Checked; NeedsAdmin = $t.Admin
            Action = @{ Type = 'CleanFolder'; Paths = $t.Paths; Pattern = $t.Pattern }
        })
    }
    $rbSize = Get-FolderSize -Path 'C:\$Recycle.Bin'
    if ($rbSize -gt (50 * 1MB)) {
        $null = $out.Add([pscustomobject]@{
            Category = 'Cleanup'
            Title = 'Empty the Recycle Bin'
            Detail = ('{0} reclaimable - deleted files are still on disk until the bin is emptied. They will be gone permanently.' -f (Format-Size $rbSize))
            Risk = 'Caution'; DefaultChecked = $false; NeedsAdmin = $false
            Action = @{ Type = 'ClearRecycleBin' }
        })
    }
    if (Test-Path -LiteralPath 'C:\Windows.old') {
        $woSize = Get-FolderSize -Path 'C:\Windows.old'
        $null = $out.Add([pscustomobject]@{
            Category = 'Cleanup'
            Title = 'Remove previous Windows installation (Windows.old)'
            Detail = ('{0} reclaimable - leftover files from a Windows upgrade. Applying opens Disk Cleanup so Windows removes it safely.' -f (Format-Size $woSize))
            Risk = 'Caution'; DefaultChecked = $false; NeedsAdmin = $true
            Action = @{ Type = 'OpenDiskCleanup' }
        })
    }
    return $out
}

function Get-LoadFindings {
    $out = New-Object System.Collections.ArrayList
    $cores = [Environment]::ProcessorCount
    $first = @{}
    foreach ($p in @(Get-Process -ErrorAction SilentlyContinue)) {
        try { $first[$p.Id] = $p.TotalProcessorTime.TotalMilliseconds } catch { }
    }
    Start-Sleep -Milliseconds 1000
    $rows = New-Object System.Collections.ArrayList
    foreach ($p in @(Get-Process -ErrorAction SilentlyContinue)) {
        try { $t = $p.TotalProcessorTime.TotalMilliseconds } catch { continue }
        if (-not $first.ContainsKey($p.Id)) { continue }
        $delta = $t - $first[$p.Id]
        $pct = [math]::Round(($delta / (1000.0 * $cores)) * 100, 1)
        if ($pct -lt 5) { continue }
        $name = $p.ProcessName
        if ($script:ProtectedProcesses -contains $name.ToLower()) { continue }
        $ram = 0
        try { $ram = [math]::Round($p.WorkingSet64 / 1MB) } catch { }
        [void]$rows.Add([pscustomobject]@{ Name = $name; Id = $p.Id; Pct = $pct; RamMB = $ram })
    }
    foreach ($r in @($rows | Sort-Object Pct -Descending | Select-Object -First 6)) {
        $null = $out.Add([pscustomobject]@{
            Category = 'Now'
            Title = ('High CPU right now: {0}' -f $r.Name)
            Detail = ('Using about {0}% CPU ({1} MB RAM). If you do not need it open, ending it frees CPU immediately.' -f $r.Pct, $r.RamMB)
            Risk = 'Caution'; DefaultChecked = $false; NeedsAdmin = $false
            Action = @{ Type = 'KillProcess'; Id = $r.Id; Name = $r.Name }
        })
    }
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $usedPct = [int]((($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / $os.TotalVisibleMemorySize) * 100)
        if ($usedPct -ge 85) {
            $topRam = @(Get-Process -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1)
            if ($topRam.Count -gt 0) {
                $p = $topRam[0]
                $name = $p.ProcessName
                if (-not ($script:ProtectedProcesses -contains $name.ToLower())) {
                    $null = $out.Add([pscustomobject]@{
                        Category = 'Now'
                        Title = ('Memory hog: {0}' -f $name)
                        Detail = ('Memory is {0}% full and this process holds {1} MB. Consider closing it if you are not using it.' -f $usedPct, [math]::Round($p.WorkingSet64 / 1MB))
                        Risk = 'Caution'; DefaultChecked = $false; NeedsAdmin = $false
                        Action = @{ Type = 'KillProcess'; Id = $p.Id; Name = $name }
                    })
                }
            }
        }
    } catch { }
    return $out
}

function Invoke-FixAction {
    param($Finding)
    $a = $Finding.Action
    $result = [pscustomobject]@{ Status = 'Failed'; Note = 'Unknown action type' }
    try {
        switch ($a.Type) {
            'RegRemoved' {
                $ok = Remove-RegValueTracked -Hive $a.Hive -SubKey $a.SubKey -Name $a.Name -Track
                if ($ok) { $result = [pscustomobject]@{ Status = 'Applied'; Note = 'Disabled auto-start' } }
                else { $result = [pscustomobject]@{ Status = 'Skipped'; Note = 'Entry no longer exists' } }
            }
            'RegSet' {
                Set-RegValue -Hive $a.Hive -SubKey $a.SubKey -Name $a.Name -Value $a.Value -Kind $a.Kind -Track
                $result = [pscustomobject]@{ Status = 'Applied'; Note = 'Value set' }
            }
            'Service' {
                $svc = Get-Service -Name $a.Name -ErrorAction Stop
                $cim = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f $a.Name) -ErrorAction SilentlyContinue
                $oldStart = 'Manual'
                if ($cim) { $oldStart = $cim.StartMode }
                $wasRunning = ($svc.Status -eq 'Running')
                Set-Service -Name $a.Name -StartupType $a.Target -ErrorAction Stop
                [void]$script:SessionUndo.Add([pscustomobject]@{ Type = 'Service'; Name = $a.Name; OldStartType = $oldStart; WasRunning = $wasRunning })
                if ($a.Stop -and $wasRunning) { Stop-Service -Name $a.Name -Force -ErrorAction SilentlyContinue }
                $result = [pscustomobject]@{ Status = 'Applied'; Note = ('Set {0} to {1} (was {2})' -f $a.Name, $a.Target, $oldStart) }
            }
            'ScheduledTask' {
                Disable-ScheduledTask -TaskPath $a.Path -TaskName $a.Name -ErrorAction Stop | Out-Null
                [void]$script:SessionUndo.Add([pscustomobject]@{ Type = 'ScheduledTask'; Path = $a.Path; Name = $a.Name })
                $result = [pscustomobject]@{ Status = 'Applied'; Note = 'Task disabled' }
            }
            'CleanFolder' {
                $paths = @($a.Paths)
                $pattern = '*'
                if ($a.Pattern) { $pattern = $a.Pattern }
                $before = 0
                foreach ($p in $paths) { $before += Get-FolderSize -Path $p -Pattern $pattern }
                foreach ($p in $paths) {
                    if (-not (Test-Path -LiteralPath $p)) { continue }
                    $items = @(Get-ChildItem -LiteralPath $p -Force -Filter $pattern -ErrorAction SilentlyContinue)
                    foreach ($it in $items) { Remove-Item -LiteralPath $it.FullName -Recurse -Force -ErrorAction SilentlyContinue }
                }
                $after = 0
                foreach ($p in $paths) { $after += Get-FolderSize -Path $p -Pattern $pattern }
                $freed = [long]($before - $after)
                if ($freed -lt 0) { $freed = 0 }
                $result = [pscustomobject]@{ Status = 'Applied'; Note = ('Freed about ' + (Format-Size $freed)) }
            }
            'ClearRecycleBin' {
                $before = Get-FolderSize -Path 'C:\$Recycle.Bin'
                Clear-RecycleBin -Force -ErrorAction SilentlyContinue
                $after = Get-FolderSize -Path 'C:\$Recycle.Bin'
                $freed = [long]($before - $after)
                if ($freed -lt 0) { $freed = 0 }
                $result = [pscustomobject]@{ Status = 'Applied'; Note = ('Freed about ' + (Format-Size $freed)) }
            }
            'PowerPlan' {
                $curOut = (powercfg /getactivescheme) -join ' '
                $curM = [regex]::Match($curOut, '([0-9a-fA-F-]{36})')
                if ($curM.Success) {
                    [void]$script:SessionUndo.Add([pscustomobject]@{ Type = 'PowerPlan'; OldGuid = $curM.Groups[1].Value })
                }
                $null = (powercfg /setactive $a.Guid)
                if ($LASTEXITCODE -ne 0) { throw 'powercfg setactive failed' }
                $result = [pscustomobject]@{ Status = 'Applied'; Note = 'Active power plan changed' }
            }
            'KillProcess' {
                $answer = [System.Windows.Forms.MessageBox]::Show(('End process "{0}" (PID {1})? Unsaved work in it will be lost.' -f $a.Name, $a.Id), 'WinOptimizer', 'YesNo', 'Warning')
                if ($answer -eq 'Yes') {
                    $proc = Get-Process -Id $a.Id -ErrorAction SilentlyContinue
                    if (-not $proc) {
                        $result = [pscustomobject]@{ Status = 'Skipped'; Note = 'Process already exited' }
                    } elseif ($proc.ProcessName -ne $a.Name) {
                        $result = [pscustomobject]@{ Status = 'Skipped'; Note = 'PID now belongs to a different process - skipped for safety' }
                    } else {
                        Stop-Process -Id $a.Id -Force -ErrorAction Stop
                        $result = [pscustomobject]@{ Status = 'Applied'; Note = ('Ended ' + $a.Name) }
                    }
                } else {
                    $result = [pscustomobject]@{ Status = 'Skipped'; Note = 'You declined' }
                }
            }
            'FileMoved' {
                $destDir = Join-Path $script:AppDir 'disabled-startup'
                if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
                $leaf = Split-Path -Path $a.Source -Leaf
                $dest = Join-Path $destDir $leaf
                if (Test-Path -LiteralPath $dest) {
                    $base = [IO.Path]::GetFileNameWithoutExtension($leaf)
                    $ext = [IO.Path]::GetExtension($leaf)
                    $n = 2
                    while (Test-Path -LiteralPath $dest) {
                        $dest = Join-Path $destDir ('{0}-{1}{2}' -f $base, $n, $ext)
                        $n++
                    }
                }
                Move-Item -LiteralPath $a.Source -Destination $dest -ErrorAction Stop
                [void]$script:SessionUndo.Add([pscustomobject]@{ Type = 'FileMoved'; Source = $a.Source; Dest = $dest })
                $result = [pscustomobject]@{ Status = 'Applied'; Note = 'Moved shortcut out of Startup' }
            }
            'OpenDiskCleanup' {
                Start-Process -FilePath 'cleanmgr.exe' -ErrorAction Stop
                $result = [pscustomobject]@{ Status = 'Applied'; Note = 'Disk Cleanup opened - check "Previous Windows installation(s)"' }
            }
        }
    } catch {
        $result = [pscustomobject]@{ Status = 'Failed'; Note = $_.Exception.Message }
    }
    return $result
}

function Save-SessionBackup {
    if ($script:SessionUndo.Count -eq 0) { return $null }
    try {
        if (-not (Test-Path -LiteralPath $script:BackupDir)) { New-Item -ItemType Directory -Path $script:BackupDir -Force | Out-Null }
        $file = Join-Path $script:BackupDir ('backup-{0}.json' -f (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
        ConvertTo-Json -InputObject @($script:SessionUndo) -Depth 6 | Set-Content -LiteralPath $file -Encoding UTF8
        return $file
    } catch {
        Write-Log ('Backup failed: ' + $_.Exception.Message)
        return $null
    }
}

function Undo-LastSession {
    if (-not (Test-Path -LiteralPath $script:BackupDir)) { return @{ Count = 0; Failed = 0; File = ''; Note = 'No backups found.' } }
    $latest = Get-ChildItem -LiteralPath $script:BackupDir -Filter 'backup-*.json' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { return @{ Count = 0; Failed = 0; File = ''; Note = 'No backups found.' } }
    $entries = @()
    try {
        $parsed = Get-Content -LiteralPath $latest.FullName -Raw | ConvertFrom-Json
        $entries = @($parsed)
    } catch { return @{ Count = 0; Failed = 0; File = $latest.Name; Note = 'Backup file could not be read.' } }
    $done = 0
    $fail = 0
    foreach ($e in $entries) {
        try {
            switch ($e.Type) {
                'RegRemoved' { Set-RegValue -Hive $e.Hive -SubKey $e.SubKey -Name $e.Name -Value $e.OldValue -Kind $e.OldKind; $done++ }
                'RegSet' {
                    if ($e.OldExists) { Set-RegValue -Hive $e.Hive -SubKey $e.SubKey -Name $e.Name -Value $e.OldValue -Kind $e.OldKind }
                    else { Remove-ItemProperty -LiteralPath ('{0}:\{1}' -f $e.Hive, $e.SubKey) -Name $e.Name -Force -ErrorAction SilentlyContinue }
                    $done++
                }
                'Service' {
                    $t = [string]$e.OldStartType
                    if ($t -eq 'Auto') { $t = 'Automatic' }
                    Set-Service -Name $e.Name -StartupType $t -ErrorAction Stop
                    if ($e.WasRunning) { Start-Service -Name $e.Name -ErrorAction SilentlyContinue }
                    $done++
                }
                'ScheduledTask' { Enable-ScheduledTask -TaskPath $e.Path -TaskName $e.Name -ErrorAction Stop | Out-Null; $done++ }
                'FileMoved' { if (Test-Path -LiteralPath $e.Dest) { Move-Item -LiteralPath $e.Dest -Destination $e.Source -Force -ErrorAction Stop; $done++ } }
                'PowerPlan' { $null = (powercfg /setactive $e.OldGuid); $done++ }
            }
        } catch {
            $fail++
            Write-Log ('Undo failed: ' + $_.Exception.Message)
        }
    }
    try { Rename-Item -LiteralPath $latest.FullName -NewName ($latest.BaseName + '-reverted.json') -ErrorAction SilentlyContinue } catch { }
    return @{ Count = $done; Failed = $fail; File = $latest.Name; Note = '' }
}

function Set-LoadColor {
    param($Label, [int]$Pct)
    if ($Pct -ge 90) { $Label.ForeColor = [System.Drawing.Color]::FromArgb(190, 30, 30) }
    elseif ($Pct -ge 70) { $Label.ForeColor = [System.Drawing.Color]::FromArgb(200, 120, 0) }
    else { $Label.ForeColor = [System.Drawing.Color]::FromArgb(0, 110, 40) }
}

function Update-MonitorUi {
    $ui = $script:UI
    $st = $script:MonState
    $cores = [Environment]::ProcessorCount
    $now = Get-Date

    $cpuPct = 0
    try { $cpuPct = [int]((Get-CimInstance Win32_Processor -ErrorAction Stop | Measure-Object -Property LoadPercentage -Average).Average) } catch { }

    $ramPct = 0
    $ramText = ''
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $usedKB = $os.TotalVisibleMemorySize - $os.FreePhysicalMemory
        $ramPct = [int](($usedKB / $os.TotalVisibleMemorySize) * 100)
        $ramText = '{0:N1} GB used of {1:N1} GB' -f ($usedKB / 1MB), ($os.TotalVisibleMemorySize / 1MB)
    } catch { }

    $diskPct = 0
    $diskText = ''
    try {
        $d = Get-PSDrive C -ErrorAction Stop
        $total = $d.Used + $d.Free
        if ($total -gt 0) { $diskPct = [int](($d.Used / $total) * 100) }
        $diskText = '{0} free of {1}' -f (Format-Size $d.Free), (Format-Size $total)
    } catch { }

    $elapsedMs = 2000.0
    if ($st.LastTick) { $elapsedMs = [math]::Max(250.0, ($now - $st.LastTick).TotalMilliseconds) }

    $next = @{}
    $rows = New-Object System.Collections.ArrayList
    foreach ($p in @(Get-Process -ErrorAction SilentlyContinue)) {
        try { $t = $p.TotalProcessorTime.TotalMilliseconds } catch { continue }
        $next[$p.Id] = $t
        if ($st.Snap.ContainsKey($p.Id)) {
            $delta = $t - $st.Snap[$p.Id]
            $pct = ($delta / ($elapsedMs * $cores)) * 100
            if ($pct -ge 0.5) {
                $ram = 0
                try { $ram = [math]::Round($p.WorkingSet64 / 1MB) } catch { }
                [void]$rows.Add([pscustomobject]@{ Name = $p.ProcessName; Id = $p.Id; Pct = [math]::Round($pct, 1); RamMB = $ram })
            }
        }
    }
    $st.Snap = $next
    $st.LastTick = $now

    $top = @($rows | Sort-Object Pct -Descending | Select-Object -First 10)

    $ui.CpuBar.Value = [math]::Min(100, [math]::Max(0, $cpuPct))
    $ui.RamBar.Value = [math]::Min(100, [math]::Max(0, $ramPct))
    $ui.DiskBar.Value = [math]::Min(100, [math]::Max(0, $diskPct))
    $ui.CpuVal.Text = ('{0}%' -f $cpuPct)
    $ui.RamVal.Text = $ramText
    $ui.DiskVal.Text = $diskText
    Set-LoadColor -Label $ui.CpuVal -Pct $cpuPct
    Set-LoadColor -Label $ui.RamVal -Pct $ramPct
    Set-LoadColor -Label $ui.DiskVal -Pct $diskPct

    $ui.Procs.BeginUpdate()
    $ui.Procs.Items.Clear()
    foreach ($r in $top) {
        $li = New-Object System.Windows.Forms.ListViewItem($r.Name)
        [void]$li.SubItems.Add([string]$r.Id)
        [void]$li.SubItems.Add(('{0}%' -f $r.Pct))
        [void]$li.SubItems.Add([string]$r.RamMB)
        [void]$ui.Procs.Items.Add($li)
    }
    $ui.Procs.EndUpdate()

    if ($ui.Alerts.Checked -and $cpuPct -ge [int]$ui.Threshold.Value) { $st.Streak++ } else { $st.Streak = 0 }
    if ($st.Streak -ge 3) {
        $canAlert = $true
        if ($st.LastAlert) { if (($now - $st.LastAlert).TotalSeconds -lt 60) { $canAlert = $false } }
        if ($canAlert) {
            $topName = 'unknown'
            if ($top.Count -gt 0) { $topName = ('{0} ({1}%)' -f $top[0].Name, $top[0].Pct) }
            try { $script:Notify.ShowBalloonTip(5000, 'High CPU usage', ('CPU has been above {0}% for 3+ checks. Top process: {1}' -f [int]$ui.Threshold.Value, $topName), [System.Windows.Forms.ToolTipIcon]::Warning) } catch { }
            $st.LastAlert = $now
        }
    }
}

function Add-FindingToListView {
    param($ListView, $Finding)
    $title = $Finding.Title
    if ($Finding.NeedsAdmin -and -not $script:IsAdmin) { $title = $title + '  [admin]' }
    $li = New-Object System.Windows.Forms.ListViewItem($title)
    [void]$li.SubItems.Add($Finding.Category)
    [void]$li.SubItems.Add($Finding.Detail)
    [void]$li.SubItems.Add($Finding.Risk)
    $li.Tag = $Finding
    $li.Checked = [bool]$Finding.DefaultChecked
    switch ($Finding.Risk) {
        'Caution' { $li.ForeColor = [System.Drawing.Color]::FromArgb(180, 95, 6) }
        'Risky' { $li.ForeColor = [System.Drawing.Color]::FromArgb(190, 30, 30) }
        'Info' { $li.ForeColor = [System.Drawing.Color]::FromArgb(110, 110, 110) }
    }
    [void]$ListView.Items.Add($li)
}

function Build-MainWindow {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
    [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)

    $font = New-Object System.Drawing.Font('Segoe UI', 9)

    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'WinOptimizer'
    $form.Size = New-Object System.Drawing.Size(980, 680)
    $form.MinimumSize = New-Object System.Drawing.Size(860, 560)
    $form.StartPosition = 'CenterScreen'
    $form.Font = $font

    $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    if ($form.Width -gt ($wa.Width - 20)) { $form.Width = [int]($wa.Width - 20) }
    if ($form.Height -gt ($wa.Height - 20)) { $form.Height = [int]($wa.Height - 20) }

    $tabs = New-Object System.Windows.Forms.TabControl
    $tabs.Dock = 'Fill'
    $form.Controls.Add($tabs)

    $page1 = New-Object System.Windows.Forms.TabPage
    $page1.Text = 'Scan & Fix'
    [void]$tabs.TabPages.Add($page1)

    $panelTop = New-Object System.Windows.Forms.Panel
    $panelTop.Dock = 'Top'
    $panelTop.Height = 82

    $btnScan = New-Object System.Windows.Forms.Button
    $btnScan.Text = 'Scan my PC'
    $btnScan.Location = New-Object System.Drawing.Point(8, 8)
    $btnScan.Size = New-Object System.Drawing.Size(120, 28)
    $panelTop.Controls.Add($btnScan)

    $btnSafe = New-Object System.Windows.Forms.Button
    $btnSafe.Text = 'Check Safe Only'
    $btnSafe.Location = New-Object System.Drawing.Point(136, 8)
    $btnSafe.Size = New-Object System.Drawing.Size(112, 28)
    $panelTop.Controls.Add($btnSafe)

    $btnNothing = New-Object System.Windows.Forms.Button
    $btnNothing.Text = 'Uncheck All'
    $btnNothing.Location = New-Object System.Drawing.Point(256, 8)
    $btnNothing.Size = New-Object System.Drawing.Size(92, 28)
    $panelTop.Controls.Add($btnNothing)

    $btnAdmin = New-Object System.Windows.Forms.Button
    $btnAdmin.Text = 'Restart as Admin'
    $btnAdmin.Location = New-Object System.Drawing.Point(356, 8)
    $btnAdmin.Size = New-Object System.Drawing.Size(130, 28)
    $btnAdmin.Visible = -not $script:IsAdmin
    $panelTop.Controls.Add($btnAdmin)

    $lblScanStatus = New-Object System.Windows.Forms.Label
    $lblScanStatus.Location = New-Object System.Drawing.Point(8, 42)
    $lblScanStatus.AutoSize = $true
    if ($script:IsAdmin) { $lblScanStatus.Text = 'Ready (running as admin - all fixes available).' }
    else { $lblScanStatus.Text = 'Ready. Not running as admin, so a few fixes will be skipped; use "Restart as Admin".' }
    $panelTop.Controls.Add($lblScanStatus)

    $prog = New-Object System.Windows.Forms.ProgressBar
    $prog.Location = New-Object System.Drawing.Point(8, 62)
    $prog.Size = New-Object System.Drawing.Size(240, 14)
    $prog.Visible = $false
    $panelTop.Controls.Add($prog)

    $panelBottom = New-Object System.Windows.Forms.Panel
    $panelBottom.Dock = 'Bottom'
    $panelBottom.Height = 46

    $btnApply = New-Object System.Windows.Forms.Button
    $btnApply.Text = 'Apply Selected Fixes'
    $btnApply.Location = New-Object System.Drawing.Point(8, 8)
    $btnApply.Size = New-Object System.Drawing.Size(150, 30)
    $panelBottom.Controls.Add($btnApply)

    $btnUndo = New-Object System.Windows.Forms.Button
    $btnUndo.Text = 'Undo Last Changes'
    $btnUndo.Location = New-Object System.Drawing.Point(166, 8)
    $btnUndo.Size = New-Object System.Drawing.Size(150, 30)
    $panelBottom.Controls.Add($btnUndo)

    $lblResult = New-Object System.Windows.Forms.Label
    $lblResult.Location = New-Object System.Drawing.Point(330, 14)
    $lblResult.AutoSize = $true
    $lblResult.Text = ''
    $panelBottom.Controls.Add($lblResult)

    $lv = New-Object System.Windows.Forms.ListView
    $lv.View = 'Details'
    $lv.CheckBoxes = $true
    $lv.FullRowSelect = $true
    $lv.GridLines = $true
    $lv.HideSelection = $false
    $lv.MultiSelect = $false
    [void]$lv.Columns.Add('Finding', 280)
    [void]$lv.Columns.Add('Category', 80)
    [void]$lv.Columns.Add('What it does', 480)
    [void]$lv.Columns.Add('Risk', 70)
    $lv.Dock = 'Fill'

    $page1.Controls.Add($lv)
    $page1.Controls.Add($panelBottom)
    $page1.Controls.Add($panelTop)

    $page2 = New-Object System.Windows.Forms.TabPage
    $page2.Text = 'Monitor'
    [void]$tabs.TabPages.Add($page2)

    $panelMon = New-Object System.Windows.Forms.Panel
    $panelMon.Dock = 'Top'
    $panelMon.Height = 160

    $lblCpu = New-Object System.Windows.Forms.Label
    $lblCpu.Text = 'CPU'
    $lblCpu.Location = New-Object System.Drawing.Point(8, 14)
    $lblCpu.AutoSize = $true
    $panelMon.Controls.Add($lblCpu)

    $barCpu = New-Object System.Windows.Forms.ProgressBar
    $barCpu.Location = New-Object System.Drawing.Point(56, 12)
    $barCpu.Size = New-Object System.Drawing.Size(420, 16)
    $panelMon.Controls.Add($barCpu)

    $lblCpuVal = New-Object System.Windows.Forms.Label
    $lblCpuVal.Location = New-Object System.Drawing.Point(484, 14)
    $lblCpuVal.AutoSize = $true
    $lblCpuVal.Text = '...'
    $panelMon.Controls.Add($lblCpuVal)

    $lblRam = New-Object System.Windows.Forms.Label
    $lblRam.Text = 'RAM'
    $lblRam.Location = New-Object System.Drawing.Point(8, 44)
    $lblRam.AutoSize = $true
    $panelMon.Controls.Add($lblRam)

    $barRam = New-Object System.Windows.Forms.ProgressBar
    $barRam.Location = New-Object System.Drawing.Point(56, 42)
    $barRam.Size = New-Object System.Drawing.Size(420, 16)
    $panelMon.Controls.Add($barRam)

    $lblRamVal = New-Object System.Windows.Forms.Label
    $lblRamVal.Location = New-Object System.Drawing.Point(484, 44)
    $lblRamVal.AutoSize = $true
    $lblRamVal.Text = '...'
    $panelMon.Controls.Add($lblRamVal)

    $lblDisk = New-Object System.Windows.Forms.Label
    $lblDisk.Text = 'Disk C:'
    $lblDisk.Location = New-Object System.Drawing.Point(8, 74)
    $lblDisk.AutoSize = $true
    $panelMon.Controls.Add($lblDisk)

    $barDisk = New-Object System.Windows.Forms.ProgressBar
    $barDisk.Location = New-Object System.Drawing.Point(56, 72)
    $barDisk.Size = New-Object System.Drawing.Size(420, 16)
    $panelMon.Controls.Add($barDisk)

    $lblDiskVal = New-Object System.Windows.Forms.Label
    $lblDiskVal.Location = New-Object System.Drawing.Point(484, 74)
    $lblDiskVal.AutoSize = $true
    $lblDiskVal.Text = '...'
    $panelMon.Controls.Add($lblDiskVal)

    $lblPower = New-Object System.Windows.Forms.Label
    $lblPower.Location = New-Object System.Drawing.Point(8, 102)
    $lblPower.AutoSize = $true
    $lblPower.Text = 'Power plan: run monitoring to show'
    $panelMon.Controls.Add($lblPower)

    $btnMon = New-Object System.Windows.Forms.Button
    $btnMon.Text = 'Start monitoring'
    $btnMon.Location = New-Object System.Drawing.Point(8, 124)
    $btnMon.Size = New-Object System.Drawing.Size(150, 28)
    $panelMon.Controls.Add($btnMon)

    $chkAlerts = New-Object System.Windows.Forms.CheckBox
    $chkAlerts.Text = 'Alerts on'
    $chkAlerts.Location = New-Object System.Drawing.Point(170, 128)
    $chkAlerts.AutoSize = $true
    $chkAlerts.Checked = $true
    $panelMon.Controls.Add($chkAlerts)

    $numThreshold = New-Object System.Windows.Forms.NumericUpDown
    $numThreshold.Location = New-Object System.Drawing.Point(256, 127)
    $numThreshold.Size = New-Object System.Drawing.Size(60, 22)
    $numThreshold.Minimum = 50
    $numThreshold.Maximum = 100
    $numThreshold.Value = 85
    $panelMon.Controls.Add($numThreshold)

    $lblThresh = New-Object System.Windows.Forms.Label
    $lblThresh.Text = '% CPU - alerts when that is sustained'
    $lblThresh.Location = New-Object System.Drawing.Point(322, 130)
    $lblThresh.AutoSize = $true
    $panelMon.Controls.Add($lblThresh)

    $lvProcs = New-Object System.Windows.Forms.ListView
    $lvProcs.View = 'Details'
    $lvProcs.FullRowSelect = $true
    $lvProcs.GridLines = $true
    [void]$lvProcs.Columns.Add('Process', 240)
    [void]$lvProcs.Columns.Add('PID', 70)
    [void]$lvProcs.Columns.Add('CPU', 80)
    [void]$lvProcs.Columns.Add('RAM (MB)', 100)
    $lvProcs.Dock = 'Fill'

    $page2.Controls.Add($lvProcs)
    $page2.Controls.Add($panelMon)

    $notify = New-Object System.Windows.Forms.NotifyIcon
    $notify.Icon = [System.Drawing.SystemIcons]::Application
    $notify.Text = 'WinOptimizer'
    $notify.Visible = $true
    $notify.Add_DoubleClick({ $form.Show(); $form.WindowState = 'Normal'; $form.Activate() })
    $script:Notify = $notify

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 2000
    $timer.Add_Tick({ try { Update-MonitorUi } catch { Write-Log ('Monitor error: ' + $_.Exception.Message) } })
    $script:MonTimer = $timer

    $form.Add_FormClosed({
        try { $script:MonTimer.Stop() } catch { }
        try { $script:Notify.Visible = $false; $script:Notify.Dispose() } catch { }
    })

    $btnScan.Add_Click({
        $ui = $script:UI
        $ui.ScanBtn.Enabled = $false
        $ui.Lv.Items.Clear()
        $ui.Prog.Visible = $true
        $ui.Prog.Value = 0
        $ui.Status.Text = 'Scanning...'
        $ui.Result.Text = ''
        [System.Windows.Forms.Application]::DoEvents()
        $steps = @(
            @{ Name = 'System info'; Fn = { Get-SystemInfoFindings } },
            @{ Name = 'Startup items'; Fn = { Get-StartupFindings } },
            @{ Name = 'Services'; Fn = { Get-ServiceFindings } },
            @{ Name = 'Scheduled tasks'; Fn = { Get-TaskFindings } },
            @{ Name = 'Windows tweaks'; Fn = { Get-TweakFindings } },
            @{ Name = 'Disk cleanup'; Fn = { Get-CleanupFindings } },
            @{ Name = 'Current load'; Fn = { Get-LoadFindings } }
        )
        $i = 0
        foreach ($s in $steps) {
            $i++
            $ui.Status.Text = ('Scanning: {0}...' -f $s.Name)
            [System.Windows.Forms.Application]::DoEvents()
            $findings = @(& $s.Fn)
            foreach ($f in $findings) { Add-FindingToListView -ListView $ui.Lv -Finding $f }
            $ui.Prog.Value = [int](($i / $steps.Count) * 100)
            [System.Windows.Forms.Application]::DoEvents()
        }
        $ui.Prog.Visible = $false
        $ui.Status.Text = ('Scan complete: {0} findings. Checked items get applied; "Check Safe Only" resets to the safest set.' -f $ui.Lv.Items.Count)
        $ui.ScanBtn.Text = 'Scan again'
        $ui.ScanBtn.Enabled = $true
    })

    $btnSafe.Add_Click({
        foreach ($li in $script:UI.Lv.Items) {
            if ($li.Tag) { $li.Checked = ($li.Tag.Risk -eq 'Safe') }
        }
    })

    $btnNothing.Add_Click({
        foreach ($li in $script:UI.Lv.Items) { $li.Checked = $false }
    })

    $btnAdmin.Add_Click({
        Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath)) -Verb RunAs
        $form.Close()
    })

    $btnApply.Add_Click({
        $ui = $script:UI
        $selected = @($ui.Lv.CheckedItems)
        $actionable = @($selected | Where-Object { $_.Tag -and $_.Tag.Action })
        if ($actionable.Count -eq 0) {
            [void][System.Windows.Forms.MessageBox]::Show($ui.Form, 'No fixes are checked. Review the list and check the items you want to apply.', 'WinOptimizer', 'OK', 'Information')
            return
        }
        $ans = [System.Windows.Forms.MessageBox]::Show($ui.Form, ('Apply {0} selected fix(es)? A backup is saved first so most changes can be undone. Browser-cache, Recycle-Bin and End-process items will ask for confirmation.' -f $actionable.Count), 'WinOptimizer', 'YesNo', 'Question')
        if ($ans -ne 'Yes') { return }

        $script:SessionUndo.Clear()
        $applied = 0
        $skipped = 0
        $failed = 0
        $toRemove = New-Object System.Collections.ArrayList
        foreach ($li in $actionable) {
            $f = $li.Tag
            if ($f.NeedsAdmin -and -not $script:IsAdmin) {
                $skipped++
                Write-Log ('Skipped (needs admin): ' + $f.Title)
                continue
            }
            $r = Invoke-FixAction -Finding $f
            Write-Log ('{0}: {1} - {2}' -f $r.Status, $f.Title, $r.Note)
            if ($r.Status -eq 'Applied') { $applied++; [void]$toRemove.Add($li) }
            elseif ($r.Status -eq 'Skipped') { $skipped++ }
            else { $failed++ }
        }
        foreach ($li in $toRemove) { $ui.Lv.Items.Remove($li) }
        $backupFile = Save-SessionBackup
        $msgParts = @()
        $msgParts += ('Applied {0}' -f $applied)
        if ($skipped -gt 0) { $msgParts += ('skipped {0}' -f $skipped) }
        if ($failed -gt 0) { $msgParts += ('failed {0}' -f $failed) }
        $msg = ($msgParts -join ', ') + '.'
        if ($skipped -gt 0 -and -not $script:IsAdmin) { $msg += ' Items marked [admin] need "Restart as Admin".' }
        if ($backupFile) { $msg += ' Undo is available.' }
        $ui.Result.Text = $msg
    })

    $btnUndo.Add_Click({
        $ans = [System.Windows.Forms.MessageBox]::Show($script:UI.Form, 'Undo the most recent apply session? This restores registry values, service startup types, scheduled tasks, startup shortcuts, and the power plan.', 'WinOptimizer', 'YesNo', 'Question')
        if ($ans -ne 'Yes') { return }
        $r = Undo-LastSession
        if ($r.Note) {
            [void][System.Windows.Forms.MessageBox]::Show($script:UI.Form, $r.Note, 'WinOptimizer', 'OK', 'Information')
        } else {
            [void][System.Windows.Forms.MessageBox]::Show($script:UI.Form, ('Restored {0} change(s), {1} failed. Backup file: {2}' -f $r.Count, $r.Failed, $r.File), 'WinOptimizer', 'OK', 'Information')
        }
    })

    $btnMon.Add_Click({
        if ($script:MonState.Running) {
            $script:MonTimer.Stop()
            $script:MonState.Running = $false
            $script:UI.BtnMon.Text = 'Start monitoring'
        } else {
            $script:MonState.Snap = @{}
            $script:MonState.LastTick = $null
            $script:MonState.Streak = 0
            $script:MonState.LastAlert = $null
            $script:MonState.Running = $true
            $script:MonTimer.Start()
            $script:UI.BtnMon.Text = 'Pause monitoring'
            try { Update-MonitorUi } catch { }
            try {
                $schemeOut = (powercfg /getactivescheme) -join ' '
                $script:UI.Power.Text = 'Power plan: ' + $schemeOut
            } catch { }
        }
    })

    $script:UI = @{
        Form = $form; Lv = $lv; Prog = $prog; Status = $lblScanStatus; Result = $lblResult
        ScanBtn = $btnScan; SafeBtn = $btnSafe; NothingBtn = $btnNothing; AdminBtn = $btnAdmin
        ApplyBtn = $btnApply; UndoBtn = $btnUndo
        CpuBar = $barCpu; RamBar = $barRam; DiskBar = $barDisk
        CpuVal = $lblCpuVal; RamVal = $lblRamVal; DiskVal = $lblDiskVal
        Procs = $lvProcs; Alerts = $chkAlerts; Threshold = $numThreshold; BtnMon = $btnMon; Power = $lblPower
        TopPanel = $panelTop; MonPanel = $panelMon
    }
    return $form
}

function Invoke-SelfTest {
    $results = New-Object System.Collections.ArrayList
    $origBackupDir = $script:BackupDir
    $testBackupDir = Join-Path $script:AppDir 'backups\selftest'
    if (-not (Test-Path -LiteralPath $testBackupDir)) { New-Item -ItemType Directory -Path $testBackupDir -Force | Out-Null }
    $script:BackupDir = $testBackupDir

    $sandboxPath = 'HKCU:\Software\WinOptimizerSelfTest'
    $sandboxSub = 'Software\WinOptimizerSelfTest'
    $cleanDir = Join-Path $env:TEMP 'winopt-selftest-clean'
    $srcDir1 = Join-Path $env:TEMP 'winopt-selftest-src1'
    $srcDir2 = Join-Path $env:TEMP 'winopt-selftest-src2'
    $destDir = Join-Path $script:AppDir 'disabled-startup'

    function Add-TestResult {
        param([string]$Name, [string]$Status, [string]$Note)
        [void]$results.Add([pscustomobject]@{ Name = $Name; Status = $Status; Note = $Note })
    }

    Remove-Item -LiteralPath $sandboxPath -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -Path $sandboxPath -Force | Out-Null
    Remove-Item -LiteralPath (Join-Path $destDir 'winopt-selftest-a.lnk') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $destDir 'winopt-selftest-a-2.lnk') -Force -ErrorAction SilentlyContinue

    try {
        $problems = New-Object System.Collections.ArrayList
        $script:SessionUndo.Clear()
        $f = [pscustomobject]@{ Category = 'Test'; Title = 'single entry'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'RegSet'; Hive = 'HKCU'; SubKey = $sandboxSub; Name = 'Single'; Value = 'v1'; Kind = 'String' } }
        $r = Invoke-FixAction -Finding $f
        if ($r.Status -ne 'Applied') { [void]$problems.Add('apply status: ' + $r.Status) }
        $v = Get-RegValueInfo 'HKCU' $sandboxSub 'Single'
        if (-not $v.Exists -or [string]$v.Value -ne 'v1') { [void]$problems.Add('value not written') }
        $file = Save-SessionBackup
        if (-not $file) { [void]$problems.Add('no backup file written') }
        else {
            $raw = [string](Get-Content -LiteralPath $file -Raw)
            if ($raw.TrimStart().Substring(0, 1) -ne '[') { [void]$problems.Add('single-entry backup is not a JSON array') }
        }
        $null = Undo-LastSession
        $v2 = Get-RegValueInfo 'HKCU' $sandboxSub 'Single'
        if ($v2.Exists) { [void]$problems.Add('undo did not remove value') }
        if ($problems.Count -eq 0) { Add-TestResult 'Single-entry backup round-trip' 'PASS' '' } else { Add-TestResult 'Single-entry backup round-trip' 'FAIL' ($problems -join '; ') }
    } catch { Add-TestResult 'Single-entry backup round-trip' 'FAIL' $_.Exception.Message }

    try {
        $problems = New-Object System.Collections.ArrayList
        $script:SessionUndo.Clear()
        Set-ItemProperty -LiteralPath $sandboxPath -Name 'Keep' -Value 'old' -Type String
        $f = [pscustomobject]@{ Category = 'Test'; Title = 'overwrite'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'RegSet'; Hive = 'HKCU'; SubKey = $sandboxSub; Name = 'Keep'; Value = 'new'; Kind = 'String' } }
        $r = Invoke-FixAction -Finding $f
        if ($r.Status -ne 'Applied') { [void]$problems.Add('apply status: ' + $r.Status) }
        $v = Get-RegValueInfo 'HKCU' $sandboxSub 'Keep'
        if ([string]$v.Value -ne 'new') { [void]$problems.Add('new value not written') }
        $null = Save-SessionBackup
        $null = Undo-LastSession
        $v2 = Get-RegValueInfo 'HKCU' $sandboxSub 'Keep'
        if ([string]$v2.Value -ne 'old') { [void]$problems.Add('old value not restored') }
        if ($problems.Count -eq 0) { Add-TestResult 'Registry overwrite + undo restore' 'PASS' '' } else { Add-TestResult 'Registry overwrite + undo restore' 'FAIL' ($problems -join '; ') }
    } catch { Add-TestResult 'Registry overwrite + undo restore' 'FAIL' $_.Exception.Message }

    try {
        $problems = New-Object System.Collections.ArrayList
        $script:SessionUndo.Clear()
        Set-ItemProperty -LiteralPath $sandboxPath -Name 'RemoveMe' -Value 'willgo' -Type String
        $f = [pscustomobject]@{ Category = 'Test'; Title = 'remove'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'RegRemoved'; Hive = 'HKCU'; SubKey = $sandboxSub; Name = 'RemoveMe' } }
        $r = Invoke-FixAction -Finding $f
        if ($r.Status -ne 'Applied') { [void]$problems.Add('apply status: ' + $r.Status) }
        $v = Get-RegValueInfo 'HKCU' $sandboxSub 'RemoveMe'
        if ($v.Exists) { [void]$problems.Add('value not removed') }
        $null = Save-SessionBackup
        $null = Undo-LastSession
        $v2 = Get-RegValueInfo 'HKCU' $sandboxSub 'RemoveMe'
        if (-not $v2.Exists -or [string]$v2.Value -ne 'willgo') { [void]$problems.Add('value not restored') }
        if ($problems.Count -eq 0) { Add-TestResult 'Registry remove + undo restore' 'PASS' '' } else { Add-TestResult 'Registry remove + undo restore' 'FAIL' ($problems -join '; ') }
    } catch { Add-TestResult 'Registry remove + undo restore' 'FAIL' $_.Exception.Message }

    try {
        $problems = New-Object System.Collections.ArrayList
        $script:SessionUndo.Clear()
        Remove-Item -LiteralPath $cleanDir -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path (Join-Path $cleanDir 'sub') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $cleanDir 'a.tmp') -Value ('x' * 1000)
        Set-Content -LiteralPath (Join-Path $cleanDir 'sub\b.tmp') -Value ('y' * 2000)
        $f = [pscustomobject]@{ Category = 'Test'; Title = 'clean'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'CleanFolder'; Paths = @($cleanDir); Pattern = '*' } }
        $r = Invoke-FixAction -Finding $f
        if ($r.Status -ne 'Applied') { [void]$problems.Add('apply status: ' + $r.Status) }
        $left = @(Get-ChildItem -LiteralPath $cleanDir -Force -ErrorAction SilentlyContinue)
        if ($left.Count -ne 0) { [void]$problems.Add('files left behind: ' + $left.Count) }
        if ($problems.Count -eq 0) { Add-TestResult 'Folder cleanup (nested files)' 'PASS' $r.Note } else { Add-TestResult 'Folder cleanup (nested files)' 'FAIL' ($problems -join '; ') }
    } catch { Add-TestResult 'Folder cleanup (nested files)' 'FAIL' $_.Exception.Message }

    try {
        $problems = New-Object System.Collections.ArrayList
        $script:SessionUndo.Clear()
        Remove-Item -LiteralPath $srcDir1 -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $srcDir1 -Force | Out-Null
        $srcFile1 = Join-Path $srcDir1 'winopt-selftest-a.lnk'
        Set-Content -LiteralPath $srcFile1 -Value 'one'
        $f = [pscustomobject]@{ Category = 'Test'; Title = 'move'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'FileMoved'; Source = $srcFile1 } }
        $r = Invoke-FixAction -Finding $f
        if ($r.Status -ne 'Applied') { [void]$problems.Add('apply status: ' + $r.Status) }
        if (Test-Path -LiteralPath $srcFile1) { [void]$problems.Add('source still present after move') }
        $moved = Join-Path $destDir 'winopt-selftest-a.lnk'
        if (-not (Test-Path -LiteralPath $moved)) { [void]$problems.Add('moved file missing in disabled-startup') }
        $null = Save-SessionBackup
        $null = Undo-LastSession
        if (-not (Test-Path -LiteralPath $srcFile1)) { [void]$problems.Add('undo did not restore file') }
        if ($problems.Count -eq 0) { Add-TestResult 'Startup shortcut move + undo' 'PASS' '' } else { Add-TestResult 'Startup shortcut move + undo' 'FAIL' ($problems -join '; ') }
    } catch { Add-TestResult 'Startup shortcut move + undo' 'FAIL' $_.Exception.Message }

    try {
        $problems = New-Object System.Collections.ArrayList
        $script:SessionUndo.Clear()
        if (-not (Test-Path -LiteralPath $srcDir1)) { New-Item -ItemType Directory -Path $srcDir1 -Force | Out-Null }
        Remove-Item -LiteralPath $srcDir2 -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $srcDir2 -Force | Out-Null
        $srcFile1 = Join-Path $srcDir1 'winopt-selftest-a.lnk'
        $srcFile2 = Join-Path $srcDir2 'winopt-selftest-a.lnk'
        Set-Content -LiteralPath $srcFile1 -Value 'one'
        Set-Content -LiteralPath $srcFile2 -Value 'two'
        $f1 = [pscustomobject]@{ Category = 'Test'; Title = 'move1'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'FileMoved'; Source = $srcFile1 } }
        $f2 = [pscustomobject]@{ Category = 'Test'; Title = 'move2'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'FileMoved'; Source = $srcFile2 } }
        $r1 = Invoke-FixAction -Finding $f1
        $r2 = Invoke-FixAction -Finding $f2
        if ($r1.Status -ne 'Applied' -or $r2.Status -ne 'Applied') { [void]$problems.Add(('apply status: {0}/{1}' -f $r1.Status, $r2.Status)) }
        $moved1 = Join-Path $destDir 'winopt-selftest-a.lnk'
        $moved2 = Join-Path $destDir 'winopt-selftest-a-2.lnk'
        if (-not (Test-Path -LiteralPath $moved1)) { [void]$problems.Add('first moved file missing') }
        if (-not (Test-Path -LiteralPath $moved2)) { [void]$problems.Add('second moved file missing (collision overwrite)') }
        $null = Save-SessionBackup
        $null = Undo-LastSession
        if (-not (Test-Path -LiteralPath $srcFile1)) { [void]$problems.Add('undo did not restore first file') }
        if (-not (Test-Path -LiteralPath $srcFile2)) { [void]$problems.Add('undo did not restore second file') }
        if ($problems.Count -eq 0) { Add-TestResult 'Same-name shortcut collision + undo' 'PASS' '' } else { Add-TestResult 'Same-name shortcut collision + undo' 'FAIL' ($problems -join '; ') }
    } catch { Add-TestResult 'Same-name shortcut collision + undo' 'FAIL' $_.Exception.Message }

    try {
        $problems = New-Object System.Collections.ArrayList
        $script:SessionUndo.Clear()
        $before = ((powercfg /getactivescheme) -join ' ')
        $bm = [regex]::Match($before, '([0-9a-fA-F-]{36})')
        $beforeGuid = ''
        if ($bm.Success) { $beforeGuid = $bm.Groups[1].Value }
        $hpGuid = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
        $f = [pscustomobject]@{ Category = 'Test'; Title = 'power'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'PowerPlan'; Guid = $hpGuid } }
        $r = Invoke-FixAction -Finding $f
        if ($r.Status -ne 'Applied') {
            Add-TestResult 'Power plan change + undo' 'SKIP' ('apply failed: ' + $r.Note)
        } else {
            $after = ((powercfg /getactivescheme) -join ' ')
            $am = [regex]::Match($after, '([0-9a-fA-F-]{36})')
            if (-not $am.Success -or $am.Groups[1].Value -ne $hpGuid) { [void]$problems.Add('active plan did not change') }
            $null = Save-SessionBackup
            $null = Undo-LastSession
            $restored = ((powercfg /getactivescheme) -join ' ')
            $rm = [regex]::Match($restored, '([0-9a-fA-F-]{36})')
            if (-not $rm.Success -or $rm.Groups[1].Value -ne $beforeGuid) { [void]$problems.Add('undo did not restore original plan') }
            if ($problems.Count -eq 0) { Add-TestResult 'Power plan change + undo' 'PASS' '' } else { Add-TestResult 'Power plan change + undo' 'FAIL' ($problems -join '; ') }
        }
    } catch { Add-TestResult 'Power plan change + undo' 'FAIL' $_.Exception.Message }

    if (-not $script:IsAdmin) {
        Add-TestResult 'Service disable + undo' 'SKIP' 'needs admin'
    } else {
        try {
            $problems = New-Object System.Collections.ArrayList
            $cim = Get-CimInstance Win32_Service -Filter "Name='WMPNetworkSvc'" -ErrorAction SilentlyContinue
            if (-not $cim) {
                Add-TestResult 'Service disable + undo' 'SKIP' 'test service not present'
            } elseif ($cim.StartMode -eq 'Disabled') {
                Add-TestResult 'Service disable + undo' 'SKIP' 'already disabled'
            } else {
                $script:SessionUndo.Clear()
                $orig = $cim.StartMode
                $f = [pscustomobject]@{ Category = 'Test'; Title = 'svc'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'Service'; Name = 'WMPNetworkSvc'; Target = 'Disabled'; Stop = $true } }
                $r = Invoke-FixAction -Finding $f
                if ($r.Status -ne 'Applied') { [void]$problems.Add('apply status: ' + $r.Status) }
                $cim2 = Get-CimInstance Win32_Service -Filter "Name='WMPNetworkSvc'" -ErrorAction SilentlyContinue
                if (-not $cim2 -or $cim2.StartMode -ne 'Disabled') { [void]$problems.Add('service not disabled') }
                $null = Save-SessionBackup
                $null = Undo-LastSession
                $cim3 = Get-CimInstance Win32_Service -Filter "Name='WMPNetworkSvc'" -ErrorAction SilentlyContinue
                if (-not $cim3 -or $cim3.StartMode -ne $orig) { [void]$problems.Add('undo did not restore start mode') }
                if ($problems.Count -eq 0) { Add-TestResult 'Service disable + undo' 'PASS' '' } else { Add-TestResult 'Service disable + undo' 'FAIL' ($problems -join '; ') }
            }
        } catch { Add-TestResult 'Service disable + undo' 'FAIL' $_.Exception.Message }
    }

    if (-not $script:IsAdmin) {
        Add-TestResult 'Scheduled task disable + undo' 'SKIP' 'needs admin'
    } else {
        try {
            $problems = New-Object System.Collections.ArrayList
            $task = Get-ScheduledTask -TaskPath '\Microsoft\Windows\Autochk\' -TaskName 'Proxy' -ErrorAction SilentlyContinue
            if (-not $task) {
                Add-TestResult 'Scheduled task disable + undo' 'SKIP' 'test task not present'
            } elseif ($task.State -eq 'Disabled') {
                Add-TestResult 'Scheduled task disable + undo' 'SKIP' 'already disabled'
            } else {
                $script:SessionUndo.Clear()
                $f = [pscustomobject]@{ Category = 'Test'; Title = 'task'; Detail = ''; Risk = 'Safe'; DefaultChecked = $false; NeedsAdmin = $false; Action = @{ Type = 'ScheduledTask'; Path = '\Microsoft\Windows\Autochk\'; Name = 'Proxy' } }
                $r = Invoke-FixAction -Finding $f
                if ($r.Status -ne 'Applied') { [void]$problems.Add('apply status: ' + $r.Status) }
                $task2 = Get-ScheduledTask -TaskPath '\Microsoft\Windows\Autochk\' -TaskName 'Proxy' -ErrorAction SilentlyContinue
                if (-not $task2 -or $task2.State -ne 'Disabled') { [void]$problems.Add('task not disabled') }
                $null = Save-SessionBackup
                $null = Undo-LastSession
                $task3 = Get-ScheduledTask -TaskPath '\Microsoft\Windows\Autochk\' -TaskName 'Proxy' -ErrorAction SilentlyContinue
                if (-not $task3 -or $task3.State -eq 'Disabled') { [void]$problems.Add('undo did not re-enable task') }
                if ($problems.Count -eq 0) { Add-TestResult 'Scheduled task disable + undo' 'PASS' '' } else { Add-TestResult 'Scheduled task disable + undo' 'FAIL' ($problems -join '; ') }
            }
        } catch { Add-TestResult 'Scheduled task disable + undo' 'FAIL' $_.Exception.Message }
    }

    try {
        $script:SessionUndo.Clear()
        Remove-Item -LiteralPath $sandboxPath -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $cleanDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $srcDir1 -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $srcDir2 -Recurse -Force -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $destDir -Filter 'winopt-selftest-*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $testBackupDir -Recurse -Force -ErrorAction SilentlyContinue
        $script:BackupDir = $origBackupDir
    } catch { }

    $fail = 0
    $pass = 0
    $skip = 0
    foreach ($res in $results) {
        [Console]::WriteLine(('{0,-4} | {1} | {2}' -f $res.Status, $res.Name, $res.Note))
        if ($res.Status -eq 'FAIL') { $fail++ }
        elseif ($res.Status -eq 'SKIP') { $skip++ }
        else { $pass++ }
    }
    [Console]::WriteLine(('Self-test complete: {0} PASS, {1} FAIL, {2} SKIP' -f $pass, $fail, $skip))
    if ($fail -gt 0) { return 1 }
    return 0
}

if ($SelfTest) {
    $code = Invoke-SelfTest
    exit $code
}

if ($Console) {
    $all = New-Object System.Collections.ArrayList
    $steps = @(
        @{ Name = 'System info'; Fn = { Get-SystemInfoFindings } },
        @{ Name = 'Startup items'; Fn = { Get-StartupFindings } },
        @{ Name = 'Services'; Fn = { Get-ServiceFindings } },
        @{ Name = 'Scheduled tasks'; Fn = { Get-TaskFindings } },
        @{ Name = 'Windows tweaks'; Fn = { Get-TweakFindings } },
        @{ Name = 'Disk cleanup'; Fn = { Get-CleanupFindings } },
        @{ Name = 'Current load'; Fn = { Get-LoadFindings } }
    )
    foreach ($s in $steps) {
        Write-Output ('--- ' + $s.Name + ' ---')
        foreach ($f in @(& $s.Fn)) { [void]$all.Add($f) }
    }
    Write-Output '================'
    Write-Output ('Total findings: {0}' -f $all.Count)
    Write-Output '================'
    foreach ($f in $all) {
        '{0,-8} | {1,-8} | {2} | {3}' -f $f.Risk, $f.Category, $f.Title, $f.Detail
    }
    exit 0
}

$form = Build-MainWindow

if ($UiSmokeTest) {
    $form.Add_Shown({
        try {
            Start-Sleep -Milliseconds 200
            $ui = $script:UI
            $lines = @()
            $lines += ('Form client: {0}x{1}' -f $form.ClientSize.Width, $form.ClientSize.Height)
            $lines += ('TopPanel bounds: {0}' -f $ui.TopPanel.Bounds.ToString())
            $lines += ('Lv bounds: {0}' -f $ui.Lv.Bounds.ToString())
            $lines += ('MonPanel bounds: {0}' -f $ui.MonPanel.Bounds.ToString())
            $lines += ('Procs bounds: {0}' -f $ui.Procs.Bounds.ToString())
            $lines += ('Status bounds: {0}' -f $ui.Status.Bounds.ToString())
            Set-Content -LiteralPath (Join-Path $env:TEMP 'winopt-smoke.txt') -Value $lines
        } catch {
            Set-Content -LiteralPath (Join-Path $env:TEMP 'winopt-smoke.txt') -Value ('Smoke error: ' + $_.Exception.Message)
        }
        $form.Close()
    })
    [void]$form.ShowDialog()
    $smokeOut = Get-Content -LiteralPath (Join-Path $env:TEMP 'winopt-smoke.txt') -ErrorAction SilentlyContinue
    if ($smokeOut) { $smokeOut }
    Write-Output 'UI smoke test OK'
    exit 0
}

[void]$form.ShowDialog()
