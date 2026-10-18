using System.Diagnostics;
using System.Text.Json;
using System.ServiceProcess;
using OpenWindows.Models;
using TS = Microsoft.Win32.TaskScheduler;

namespace OpenWindows.Services;

public sealed class FixEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<UndoEntry> session = new();

    public IReadOnlyList<UndoEntry> Session => session;

    public async Task<FixResult> ApplyAsync(
        IReadOnlyList<Finding> findings,
        IReadOnlySet<(int Id, string Name)>? approvedKills = null,
        IProgress<FixProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        session.Clear();
        var actionable = findings.Where(f => f.Action != null).ToList();
        var applied = 0;
        var skipped = 0;
        var failed = 0;
        var outcomes = new List<FixOutcome>();

        for (var i = 0; i < actionable.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finding = actionable[i];
            progress?.Report(new FixProgress { Index = i + 1, Total = actionable.Count, Title = finding.Title });

            if (finding.NeedsAdmin && !ElevationHelper.IsAdmin)
            {
                skipped++;
                outcomes.Add(new FixOutcome(finding, "Skipped", "Needs administrator - skipped."));
                Log.Write("Skipped (needs admin): " + finding.Title);
                continue;
            }

            var (status, note) = await Task.Run(
                () => ApplyOne(finding, approvedKills), cancellationToken).ConfigureAwait(false);

            outcomes.Add(new FixOutcome(finding, status, note));
            Log.Write($"{status}: {finding.Title} - {note}");
            switch (status)
            {
                case "Applied":
                    applied++;
                    break;
                case "Skipped":
                    skipped++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        var backupPath = SaveSessionBackup();
        return new FixResult
        {
            Applied = applied,
            Skipped = skipped,
            Failed = failed,
            BackupPath = backupPath,
            Outcomes = outcomes
        };
    }

    private (string Status, string Note) ApplyOne(
        Finding finding, IReadOnlySet<(int Id, string Name)>? approvedKills)
    {
        try
        {
            return finding.Action switch
            {
                RegRemoveAction a => ApplyRegRemove(a),
                RegSetAction a => ApplyRegSet(a),
                ServiceAction a => ApplyService(a),
                ScheduledTaskAction a => ApplyScheduledTask(a),
                CleanFolderAction a => ApplyCleanFolder(a),
                ClearRecycleBinAction => ApplyClearRecycleBin(),
                PowerPlanAction a => ApplyPowerPlan(a),
                KillProcessAction a => ApplyKillProcess(a, approvedKills),
                MoveFileAction a => ApplyMoveFile(a),
                OpenDiskCleanupAction => ApplyOpenDiskCleanup(),
                _ => ("Failed", "Unknown action type")
            };
        }
        catch (Exception ex)
        {
            return ("Failed", ex.Message);
        }
    }

    private (string, string) ApplyRegRemove(RegRemoveAction a)
    {
        var old = RegistryUtil.GetValueInfo(a.Hive, a.SubKey, a.Name);
        if (!old.Exists)
        {
            return ("Skipped", "Entry no longer exists");
        }
        if (!RegistryUtil.RemoveValue(a.Hive, a.SubKey, a.Name))
        {
            return ("Skipped", "Entry no longer exists");
        }
        session.Add(new UndoEntry
        {
            Type = "RegRemoved",
            Hive = a.Hive,
            SubKey = a.SubKey,
            Name = a.Name,
            OldValue = JsonSerializer.SerializeToElement(old.Value),
            OldKind = old.Kind?.ToString()
        });
        return ("Applied", "Disabled auto-start");
    }

    private (string, string) ApplyRegSet(RegSetAction a)
    {
        var old = RegistryUtil.GetValueInfo(a.Hive, a.SubKey, a.Name);
        RegistryUtil.SetValue(a.Hive, a.SubKey, a.Name, a.Value, RegistryUtil.ParseKind(a.Kind));
        session.Add(new UndoEntry
        {
            Type = "RegSet",
            Hive = a.Hive,
            SubKey = a.SubKey,
            Name = a.Name,
            OldExists = old.Exists,
            OldValue = JsonSerializer.SerializeToElement(old.Value),
            OldKind = old.Kind?.ToString()
        });
        return ("Applied", "Value set");
    }

    private (string, string) ApplyService(ServiceAction a)
    {
        var oldStartMode = ServiceControl.GetStartType(a.Name);
        var wasRunning = ServiceControl.IsRunning(a.Name);
        ServiceControl.SetStartType(a.Name, ServiceControl.ParseStartMode(a.TargetStartType));
        session.Add(new UndoEntry
        {
            Type = "Service",
            Name = a.Name,
            OldStartType = ServiceControl.StartModeToString(oldStartMode),
            WasRunning = wasRunning
        });
        if (a.Stop && wasRunning)
        {
            ServiceControl.TryStop(a.Name);
        }
        return ("Applied", $"Set {a.Name} to {a.TargetStartType} (was {ServiceControl.StartModeToString(oldStartMode)})");
    }

    private (string, string) ApplyScheduledTask(ScheduledTaskAction a)
    {
        using var taskService = new TS.TaskService();
        var fullPath = a.Path.TrimEnd('\\') + "\\" + a.Name;
        var task = taskService.GetTask(fullPath);
        if (task == null)
        {
            return ("Skipped", "Task not found");
        }
        if (!task.Definition.Settings.Enabled)
        {
            return ("Skipped", "Task already disabled");
        }
        task.Definition.Settings.Enabled = false;
        task.RegisterChanges();
        session.Add(new UndoEntry { Type = "ScheduledTask", Path = a.Path, Name = a.Name });
        return ("Applied", "Task disabled");
    }

    private (string, string) ApplyCleanFolder(CleanFolderAction a)
    {
        long before = 0;
        foreach (var path in a.Paths)
        {
            before += SystemInfoService.GetFolderSize(path, a.Pattern);
        }

        foreach (var path in a.Paths)
        {
            if (!Directory.Exists(path))
            {
                continue;
            }
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.None,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            List<string> items;
            try
            {
                items = Directory.EnumerateFileSystemEntries(path, a.Pattern, options).ToList();
            }
            catch
            {
                continue;
            }
            foreach (var item in items)
            {
                TryDelete(item);
            }
        }

        long after = 0;
        foreach (var path in a.Paths)
        {
            after += SystemInfoService.GetFolderSize(path, a.Pattern);
        }
        var freed = Math.Max(0, before - after);
        return ("Applied", "Freed about " + SystemInfoService.FormatSize(freed));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path) && !File.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private (string, string) ApplyClearRecycleBin()
    {
        var before = NativeMethods.GetRecycleBinSize();
        NativeMethods.EmptyRecycleBin();
        var after = NativeMethods.GetRecycleBinSize();
        var freed = Math.Max(0, before - after);
        return ("Applied", "Freed about " + SystemInfoService.FormatSize(freed));
    }

    private (string, string) ApplyPowerPlan(PowerPlanAction a)
    {
        var currentGuid = GetActivePowerPlanGuid();
        if (!string.IsNullOrEmpty(currentGuid))
        {
            session.Add(new UndoEntry { Type = "PowerPlan", OldGuid = currentGuid });
        }
        var (exitCode, _, error) = ProcessRunner.Run("powercfg", "/setactive " + a.Guid);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("powercfg setactive failed: " + error.Trim());
        }
        return ("Applied", "Active power plan changed");
    }

    public static string GetActivePowerPlanGuid()
    {
        var (exitCode, output, _) = ProcessRunner.Run("powercfg", "/getactivescheme");
        if (exitCode != 0)
        {
            return "";
        }
        var match = System.Text.RegularExpressions.Regex.Match(output, "([0-9a-fA-F-]{36})");
        return match.Success ? match.Groups[1].Value : "";
    }

    private (string, string) ApplyKillProcess(
        KillProcessAction a, IReadOnlySet<(int Id, string Name)>? approvedKills)
    {
        if (approvedKills == null || !approvedKills.Contains((a.Id, a.Name)))
        {
            return ("Skipped", "Not approved by user");
        }
        Process? process;
        try
        {
            process = Process.GetProcessById(a.Id);
        }
        catch
        {
            return ("Skipped", "Process already exited");
        }
        using (process)
        {
            if (!string.Equals(process.ProcessName, a.Name, StringComparison.OrdinalIgnoreCase))
            {
                return ("Skipped", "PID now belongs to a different process - skipped for safety");
            }
            process.Kill();
        }
        return ("Applied", "Ended " + a.Name);
    }

    private (string, string) ApplyMoveFile(MoveFileAction a)
    {
        Directory.CreateDirectory(AppPaths.DisabledStartupDir);
        var leaf = Path.GetFileName(a.Source);
        var dest = Path.Combine(AppPaths.DisabledStartupDir, leaf);
        if (File.Exists(dest))
        {
            var baseName = Path.GetFileNameWithoutExtension(leaf);
            var ext = Path.GetExtension(leaf);
            var n = 2;
            while (File.Exists(dest))
            {
                dest = Path.Combine(AppPaths.DisabledStartupDir, $"{baseName}-{n}{ext}");
                n++;
            }
        }
        File.Move(a.Source, dest);
        session.Add(new UndoEntry { Type = "FileMoved", Source = a.Source, Dest = dest });
        return ("Applied", "Moved shortcut out of Startup");
    }

    private static (string, string) ApplyOpenDiskCleanup()
    {
        Process.Start(new ProcessStartInfo("cleanmgr.exe") { UseShellExecute = true });
        return ("Applied", "Disk Cleanup opened - check \"Previous Windows installation(s)\"");
    }

    public string? SaveSessionBackup()
    {
        if (session.Count == 0)
        {
            return null;
        }
        try
        {
            AppPaths.EnsureBackupDir();
            var file = Path.Combine(AppPaths.BackupDir,
                $"backup-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(session, JsonOptions));
            return file;
        }
        catch (Exception ex)
        {
            Log.Write("Backup failed: " + ex.Message);
            return null;
        }
    }

    public UndoResult UndoLatest()
    {
        var file = HistoryService.FindLatestBackupFile();
        if (file == null)
        {
            return new UndoResult { Count = 0, Failed = 0, Note = "No backups found." };
        }
        return UndoFile(file);
    }

    public UndoResult UndoFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        List<UndoEntry> entries;
        try
        {
            var json = File.ReadAllText(filePath);
            entries = JsonSerializer.Deserialize<List<UndoEntry>>(json) ?? new List<UndoEntry>();
        }
        catch
        {
            return new UndoResult { Count = 0, Failed = 0, FileName = fileName, Note = "Backup file could not be read." };
        }

        var done = 0;
        var failed = 0;
        foreach (var entry in entries)
        {
            try
            {
                Replay(entry);
                done++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Write("Undo failed: " + ex.Message);
            }
        }

        try
        {
            var reverted = Path.Combine(
                Path.GetDirectoryName(filePath) ?? AppPaths.BackupDir,
                Path.GetFileNameWithoutExtension(filePath) + "-reverted.json");
            File.Move(filePath, reverted);
        }
        catch
        {
        }

        return new UndoResult { Count = done, Failed = failed, FileName = fileName };
    }

    private static void Replay(UndoEntry e)
    {
        switch (e.Type)
        {
            case "RegRemoved":
                RegistryUtil.SetValue(e.Hive ?? "HKCU", e.SubKey ?? "", e.Name ?? "",
                    RegistryUtil.ConvertJsonToRegistryValue(e.OldValue, RegistryUtil.ParseKind(e.OldKind)),
                    RegistryUtil.ParseKind(e.OldKind));
                break;

            case "RegSet":
                if (e.OldExists == true)
                {
                    RegistryUtil.SetValue(e.Hive ?? "HKCU", e.SubKey ?? "", e.Name ?? "",
                        RegistryUtil.ConvertJsonToRegistryValue(e.OldValue, RegistryUtil.ParseKind(e.OldKind)),
                        RegistryUtil.ParseKind(e.OldKind));
                }
                else
                {
                    RegistryUtil.RemoveValue(e.Hive ?? "HKCU", e.SubKey ?? "", e.Name ?? "");
                }
                break;

            case "Service":
                ServiceControl.SetStartType(e.Name ?? "", ServiceControl.ParseStartMode(e.OldStartType));
                if (e.WasRunning == true)
                {
                    ServiceControl.TryStart(e.Name ?? "");
                }
                break;

            case "ScheduledTask":
                using (var taskService = new TS.TaskService())
                {
                    var fullPath = (e.Path ?? "").TrimEnd('\\') + "\\" + (e.Name ?? "");
                    var task = taskService.GetTask(fullPath)
                        ?? throw new InvalidOperationException("Task not found: " + fullPath);
                    task.Definition.Settings.Enabled = true;
                    task.RegisterChanges();
                }
                break;

            case "FileMoved":
                if (e.Dest != null && File.Exists(e.Dest) && e.Source != null)
                {
                    File.Move(e.Dest, e.Source, true);
                }
                break;

            case "PowerPlan":
                ProcessRunner.Run("powercfg", "/setactive " + (e.OldGuid ?? ""));
                break;
        }
    }
}
