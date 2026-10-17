# OpenWindows

A Windows performance improver - scans your PC, explains what is slowing it down, and fixes it safely with full undo.

Built as a native Windows desktop app (C# / .NET 8 / WPF, Fluent UI) with live system monitoring, tray icon and Windows toast notifications.

## Status

In development. The original PowerShell prototype lives in [`legacy/`](legacy/WinOptimizer/).

## Planned features

- **Dashboard** - system info and a performance health score at a glance
- **Optimize** - scans startup apps, services, scheduled tasks, Windows tweaks, disk cleanup targets and current CPU/RAM hogs; every finding is explained, risk-labelled and opt-in
- **Safe by design** - every change is backed up to JSON before applying; one click undoes a whole session (registry, services, tasks, startup shortcuts, power plan)
- **Monitor** - live CPU/RAM/disk graphs, top processes, and toast notifications when the CPU stays pegged
- **History** - browse every past apply session and undo any of them
- **Tray icon** - runs quietly in the background, one click to show

## Building

Requires the .NET 8 SDK.

```
dotnet build OpenWindows.sln -c Release
```

Headless modes (no UI, used for testing):

```
OpenWindows.exe --console     # run a scan, print findings, exit
OpenWindows.exe --selftest    # sandboxed apply/undo round-trip tests
```

## Legacy

`legacy/WinOptimizer` is the original single-file PowerShell + WinForms app this project replaces. It works, but the new app is the future.
