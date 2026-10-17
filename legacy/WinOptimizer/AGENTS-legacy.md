# AGENTS.md

Rolling notes for the coding agent (future-me) to survive context compactions. This file is written for the agent, not the user. Read this before touching the WinOptimizer project.

## Project: WinOptimizer

Single-file PowerShell 5.1 + WinForms desktop app built for the user's slow Windows laptop (Sept/Oct 2026). Two tabs:

- **Scan & Fix**: scans startup entries (Run keys + Startup folders), services, scheduled tasks, OS tweaks (power plan, visual effects, telemetry), disk cleanup targets (temp/caches/thumbnails/recycle bin/Windows.old), live CPU/RAM hogs. Findings shown in checkbox ListView with risk labels. "Apply Selected Fixes" performs changes; every mutation writes undo entries; "Undo Last Changes" reverts the latest session.
- **Monitor**: live CPU/RAM/disk bars, top processes (CPU% via TotalProcessorTime deltas between 2s ticks), tray balloon alerts when CPU stays above threshold (default 85% for 3 ticks, 60s cooldown).

## Files

- `WinOptimizer\WinOptimizer.ps1` — entire app. User rule: NO code comments, ASCII only, PS 5.1 compat (no ternary, no `??`).
- `WinOptimizer\WinOptimizer.vbs` — silent launcher (double-click this).
- `WinOptimizer\WinOptimizer.cmd` — console launcher for debugging.
- `THREEJS-REALISM-GUIDE.md` — unrelated file in repo root, do not touch.

## User's machine (verified via live scans)

- Intel i3-8145U (2c/4t) — main bottleneck. Windows 11 Pro.
- 15.8 GB RAM, typically 50-76% used.
- 256 GB NVMe, only ~14% free (app warns; low disk slows SSDs).
- Runs non-admin by default; app has "Restart as Admin" button (services/tasks/HKLM items need it).

## Design decisions (do not regress)

- Backup/undo: undo entries go to `%LOCALAPPDATA%\WinOptimizer\backups\backup-<timestamp>.json` (timestamp includes ms); log at `%LOCALAPPDATA%\WinOptimizer\optimizer.log`. Undo replays in order and renames file to `-reverted.json`.
- PS 5.1 landmine (cost us a session): `ConvertFrom-Json` does NOT enumerate top-level JSON arrays through the pipeline. `@(Get-Content -Raw | ConvertFrom-Json)` collapses a multi-entry backup into ONE object with array-valued properties. Always do `$parsed = ... | ConvertFrom-Json; $entries = @($parsed)`. This made multi-entry undo replay as a merged blob (single-entry worked by accident).
- Undo entries are recorded ONLY after the mutation succeeds (reg set/remove, service set, task disable). Failed fixes must not leave undo entries.
- `FileMoved` (startup shortcut disable) uses unique destination names on collision — the user genuinely has two "Jagex Launcher" shortcuts (user + common Startup); the old -Force move destroyed one permanently.
- `KillProcess` re-verifies the process NAME matches the scanned PID before stopping (PID-reuse guard).
- Undo entries for started services restore start type AND restart if it was running.
- Risk taxonomy: `Safe` (default checked), `Caution` (default unchecked), `Info` (no action, gray). Findings object shape: Category/Title/Detail/Risk/DefaultChecked/NeedsAdmin/Action.
- Startup items matching the `$protect` regex in `Get-StartupFindings` (security/audio/vendor/cloud-sync/VPN/backup tools) default UNchecked; others default checked.
- KillProcess findings (from `Get-LoadFindings`) always default unchecked + Yes/No confirmation at apply time. `$script:ProtectedProcesses` list must never appear as findings (`node` is deliberately NOT protected — could be user's own dev server; Caution+confirm guards it).
- WinForms dock gotcha (bit us once): controls are docked in reverse add order — the LAST control added is docked FIRST. The Dock=Fill control must be added FIRST, then edge panels. Verified via smoke test bounds.
- Output written inside WinForms event handler scriptblocks is discarded by PowerShell; smoke test diagnostics go to `%TEMP%\winopt-smoke.txt` instead.
- `-Console` switch = headless scan, prints all findings, exits 0. `-UiSmokeTest` = builds form, shows briefly, prints control bounds, exits. Both bypass GUI.

## Verification workflow (run after EVERY edit)

1. AST parse check:
   `$errs=$null; [void][System.Management.Automation.Language.Parser]::ParseFile("<path>",[ref]$null,[ref]$errs); if($errs.Count){$errs}else{'PARSE OK'}`
2. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<path>" -Console` (scan works, findings sane)
3. `powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "<path>" -UiSmokeTest`
   Expected bounds: Tab1 Lv Y=82, bottom of Lv = page height - 46. Tab2 Procs Y=160.
4. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<path>" -SelfTest`
   Sandboxed fix-engine round-trips (HKCU test key, temp folders, dummy shortcut files, power plan flip). Leaves no state; service/task tests SKIP without admin. Expect: 7 PASS / 0 FAIL / 2 SKIP.

## Status (as of last session)

- [x] PARSE OK; console scan: 48 findings on user machine
- [x] UI smoke test passes; dock-layout bug found & fixed
- [x] Self-test suite added; 7 PASS / 0 FAIL / 2 SKIP (service/task tests need admin)
- [x] Bug hunt fixes: multi-entry undo replay (ConvertFrom-Json array collapse — was broken for every real session), undo entries on failed mutations, FileMoved collision overwrite, KillProcess PID-reuse, backup filename ms
- [ ] Admin-only paths (service/task disable live) still unverified — user should use "Restart as Admin" in the GUI
- [ ] User feedback on app usability pending

## First scan results (user machine)

- Startup bloat: OneDrive, Steam, Lunar Client, Teams, Roblox, Grammarly, Firefox, Edge autolaunch, Jagex x2, Ollama, Wispr Flow, Proton Drive, Microsoft.Lists
- Services: DiagTrack Auto+Running, SysMain/WSearch/Spooler Auto, Xbox services Manual, telemetry tasks enabled
- Power plan: Balanced
- ~2.7 GB reclaimable caches (Chrome 849MB, Edge 599MB, WU cache 847MB, user temp 252MB, thumbnails 170MB)
- Disk 14% free
- 12 GB RAM in use during scan (76%)

## Future ideas (not requested yet)

- Boot-time measurement before/after
- Expand scheduled-task catalog; Delivery Optimization cache via `Delete-DeliveryOptimizationCache`
- Monitor-only tray mode; RAM trim button
- User's asks after trying app — follow up on those first
