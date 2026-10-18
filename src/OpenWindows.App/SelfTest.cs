using System.ServiceProcess;
using OpenWindows.Models;
using OpenWindows.Services;
using TS = Microsoft.Win32.TaskScheduler;

namespace OpenWindows;

public static class SelfTest
{
    private sealed record TestResult(string Name, string Status, string Note);

    public static int Run()
    {
        var results = new List<TestResult>();
        var originalBackupDir = AppPaths.BackupDirOverride;
        var testBackupDir = Path.Combine(AppPaths.AppDir, "backups", "selftest");
        Directory.CreateDirectory(testBackupDir);
        AppPaths.BackupDirOverride = testBackupDir;

        const string sandboxPath = @"HKCU:\Software\OpenWindowsSelfTest";
        const string sandboxSub = @"Software\OpenWindowsSelfTest";
        var cleanDir = Path.Combine(Path.GetTempPath(), "openwindows-selftest-clean");
        var srcDir1 = Path.Combine(Path.GetTempPath(), "openwindows-selftest-src1");
        var srcDir2 = Path.Combine(Path.GetTempPath(), "openwindows-selftest-src2");
        var destDir = AppPaths.DisabledStartupDir;

        try
        {
            RegistryUtil.RemoveValue("HKCU", sandboxSub, "Anything");

            RunTest(results, "Single-entry backup round-trip", () => TestSingleEntryRoundTrip(sandboxSub, sandboxPath));
            RunTest(results, "Registry overwrite + undo restore", () => TestRegistryOverwrite(sandboxSub, sandboxPath));
            RunTest(results, "Registry remove + undo restore", () => TestRegistryRemove(sandboxSub, sandboxPath));
            RunTest(results, "Folder cleanup (nested files)", () => TestFolderCleanup(cleanDir));
            RunTest(results, "Startup shortcut move + undo", () => TestShortcutMove(srcDir1, destDir));
            RunTest(results, "Same-name shortcut collision + undo", () => TestShortcutCollision(srcDir1, srcDir2, destDir));
            RunTest(results, "Power plan change + undo", TestPowerPlan);

            if (!ElevationHelper.IsAdmin)
            {
                results.Add(new TestResult("Service disable + undo", "SKIP", "needs admin"));
                results.Add(new TestResult("Scheduled task disable + undo", "SKIP", "needs admin"));
            }
            else
            {
                RunTest(results, "Service disable + undo", TestServiceDisable);
                RunTest(results, "Scheduled task disable + undo", TestTaskDisable);
            }
        }
        finally
        {
            try
            {
                DeleteKeyTree(sandboxPath);
                DeleteDirectory(cleanDir);
                DeleteDirectory(srcDir1);
                DeleteDirectory(srcDir2);
                if (Directory.Exists(destDir))
                {
                    foreach (var file in Directory.EnumerateFiles(destDir, "openwindows-selftest-*"))
                    {
                        TryDeleteFile(file);
                    }
                }
                DeleteDirectory(testBackupDir);
            }
            catch
            {
            }
            AppPaths.BackupDirOverride = originalBackupDir;
        }

        var fail = 0;
        var pass = 0;
        var skip = 0;
        foreach (var result in results)
        {
            Console.WriteLine($"{result.Status,-4} | {result.Name} | {result.Note}");
            switch (result.Status)
            {
                case "FAIL":
                    fail++;
                    break;
                case "SKIP":
                    skip++;
                    break;
                default:
                    pass++;
                    break;
            }
        }
        Console.WriteLine($"Self-test complete: {pass} PASS, {fail} FAIL, {skip} SKIP");
        return fail > 0 ? 1 : 0;
    }

    private static void RunTest(List<TestResult> results, string name, Func<string> test)
    {
        try
        {
            var note = test();
            if (note.StartsWith("SKIP:"))
            {
                results.Add(new TestResult(name, "SKIP", note[5..].Trim()));
            }
            else
            {
                results.Add(new TestResult(name, "PASS", note));
            }
        }
        catch (Exception ex)
        {
            results.Add(new TestResult(name, "FAIL", ex.Message));
        }
    }

    private static Finding Fix(string category, string title, FindingAction action) => new()
    {
        Category = category,
        Title = title,
        Detail = "",
        Risk = RiskLevel.Safe,
        DefaultChecked = false,
        NeedsAdmin = false,
        Action = action
    };

    private static FixResult ApplyAndBackup(params Finding[] findings)
    {
        var engine = new FixEngine();
        var result = engine.ApplyAsync(findings).GetAwaiter().GetResult();
        engine.SaveSessionBackup();
        return result;
    }

    private static void UndoLatest()
    {
        var engine = new FixEngine();
        engine.UndoLatest();
    }

    private static string TestSingleEntryRoundTrip(string sandboxSub, string sandboxPath)
    {
        var problems = new List<string>();
        var result = ApplyAndBackup(Fix("Test", "single entry",
            new RegSetAction("HKCU", sandboxSub, "Single", "v1", "String")));
        if (result.Applied != 1)
        {
            problems.Add("apply status: " + result.Applied);
        }

        var value = RegistryUtil.GetValueInfo("HKCU", sandboxSub, "Single");
        if (!value.Exists || (string?)value.Value != "v1")
        {
            problems.Add("value not written");
        }

        var latest = HistoryService.FindLatestBackupFile();
        if (latest == null)
        {
            problems.Add("no backup file written");
        }
        else
        {
            var raw = File.ReadAllText(latest).TrimStart();
            if (raw.Length == 0 || raw[0] != '[')
            {
                problems.Add("single-entry backup is not a JSON array");
            }
        }

        UndoLatest();
        var restored = RegistryUtil.GetValueInfo("HKCU", sandboxSub, "Single");
        if (restored.Exists)
        {
            problems.Add("undo did not remove value");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestRegistryOverwrite(string sandboxSub, string sandboxPath)
    {
        var problems = new List<string>();
        RegistryUtil.SetValue("HKCU", sandboxSub, "Keep", "old", Microsoft.Win32.RegistryValueKind.String);

        ApplyAndBackup(Fix("Test", "overwrite",
            new RegSetAction("HKCU", sandboxSub, "Keep", "new", "String")));

        var value = RegistryUtil.GetValueInfo("HKCU", sandboxSub, "Keep");
        if ((string?)value.Value != "new")
        {
            problems.Add("new value not written");
        }

        UndoLatest();
        var restored = RegistryUtil.GetValueInfo("HKCU", sandboxSub, "Keep");
        if ((string?)restored.Value != "old")
        {
            problems.Add("old value not restored");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestRegistryRemove(string sandboxSub, string sandboxPath)
    {
        var problems = new List<string>();
        RegistryUtil.SetValue("HKCU", sandboxSub, "RemoveMe", "willgo", Microsoft.Win32.RegistryValueKind.String);

        ApplyAndBackup(Fix("Test", "remove",
            new RegRemoveAction("HKCU", sandboxSub, "RemoveMe")));

        var value = RegistryUtil.GetValueInfo("HKCU", sandboxSub, "RemoveMe");
        if (value.Exists)
        {
            problems.Add("value not removed");
        }

        UndoLatest();
        var restored = RegistryUtil.GetValueInfo("HKCU", sandboxSub, "RemoveMe");
        if (!restored.Exists || (string?)restored.Value != "willgo")
        {
            problems.Add("value not restored");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestFolderCleanup(string cleanDir)
    {
        var problems = new List<string>();
        DeleteDirectory(cleanDir);
        Directory.CreateDirectory(Path.Combine(cleanDir, "sub"));
        File.WriteAllText(Path.Combine(cleanDir, "a.tmp"), new string('x', 1000));
        File.WriteAllText(Path.Combine(cleanDir, "sub", "b.tmp"), new string('y', 2000));

        var result = ApplyAndBackup(Fix("Test", "clean",
            new CleanFolderAction(new[] { cleanDir }, "*")));
        if (result.Applied != 1)
        {
            problems.Add("apply status: " + result.Applied);
        }

        var left = Directory.EnumerateFileSystemEntries(cleanDir).Count();
        if (left != 0)
        {
            problems.Add("files left behind: " + left);
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestShortcutMove(string srcDir1, string destDir)
    {
        var problems = new List<string>();
        DeleteDirectory(srcDir1);
        Directory.CreateDirectory(srcDir1);
        var srcFile = Path.Combine(srcDir1, "openwindows-selftest-a.lnk");
        File.WriteAllText(srcFile, "one");
        TryDeleteFile(Path.Combine(destDir, "openwindows-selftest-a.lnk"));
        TryDeleteFile(Path.Combine(destDir, "openwindows-selftest-a-2.lnk"));

        ApplyAndBackup(Fix("Test", "move", new MoveFileAction(srcFile)));

        if (File.Exists(srcFile))
        {
            problems.Add("source still present after move");
        }
        var moved = Path.Combine(destDir, "openwindows-selftest-a.lnk");
        if (!File.Exists(moved))
        {
            problems.Add("moved file missing in disabled-startup");
        }

        UndoLatest();
        if (!File.Exists(srcFile))
        {
            problems.Add("undo did not restore file");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestShortcutCollision(string srcDir1, string srcDir2, string destDir)
    {
        var problems = new List<string>();
        Directory.CreateDirectory(srcDir1);
        DeleteDirectory(srcDir2);
        Directory.CreateDirectory(srcDir2);
        var srcFile1 = Path.Combine(srcDir1, "openwindows-selftest-a.lnk");
        var srcFile2 = Path.Combine(srcDir2, "openwindows-selftest-a.lnk");
        File.WriteAllText(srcFile1, "one");
        File.WriteAllText(srcFile2, "two");
        TryDeleteFile(Path.Combine(destDir, "openwindows-selftest-a.lnk"));
        TryDeleteFile(Path.Combine(destDir, "openwindows-selftest-a-2.lnk"));

        var result = ApplyAndBackup(
            Fix("Test", "move1", new MoveFileAction(srcFile1)),
            Fix("Test", "move2", new MoveFileAction(srcFile2)));
        if (result.Applied != 2)
        {
            problems.Add("apply status: " + result.Applied);
        }

        if (!File.Exists(Path.Combine(destDir, "openwindows-selftest-a.lnk")))
        {
            problems.Add("first moved file missing");
        }
        if (!File.Exists(Path.Combine(destDir, "openwindows-selftest-a-2.lnk")))
        {
            problems.Add("second moved file missing (collision overwrite)");
        }

        UndoLatest();
        if (!File.Exists(srcFile1))
        {
            problems.Add("undo did not restore first file");
        }
        if (!File.Exists(srcFile2))
        {
            problems.Add("undo did not restore second file");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestPowerPlan()
    {
        var problems = new List<string>();
        var beforeGuid = FixEngine.GetActivePowerPlanGuid();

        var engine = new FixEngine();
        var result = engine.ApplyAsync(new[]
        {
            Fix("Test", "power", new PowerPlanAction(Catalogs.HighPerformancePlanGuid))
        }).GetAwaiter().GetResult();

        if (result.Applied != 1)
        {
            return "SKIP: apply failed (High performance plan unavailable)";
        }

        var afterGuid = FixEngine.GetActivePowerPlanGuid();
        if (!string.Equals(afterGuid, Catalogs.HighPerformancePlanGuid, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("active plan did not change");
        }

        engine.SaveSessionBackup();
        UndoLatest();
        var restoredGuid = FixEngine.GetActivePowerPlanGuid();
        if (!string.Equals(restoredGuid, beforeGuid, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("undo did not restore original plan");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestServiceDisable()
    {
        var problems = new List<string>();
        ServiceController controller;
        try
        {
            controller = new ServiceController("WMPNetworkSvc");
        }
        catch
        {
            return "SKIP: test service not present";
        }

        var original = ServiceControl.StartModeToString(controller.StartType);
        if (original == "Disabled")
        {
            return "SKIP: already disabled";
        }

        var engine = new FixEngine();
        var result = engine.ApplyAsync(new[]
        {
            Fix("Test", "svc", new ServiceAction("WMPNetworkSvc", "Disabled", true))
        }).GetAwaiter().GetResult();
        if (result.Applied != 1)
        {
            problems.Add("apply status: " + result.Applied);
        }

        var after = ServiceControl.StartModeToString(new ServiceController("WMPNetworkSvc").StartType);
        if (after != "Disabled")
        {
            problems.Add("service not disabled");
        }

        engine.SaveSessionBackup();
        UndoLatest();
        var restored = ServiceControl.StartModeToString(new ServiceController("WMPNetworkSvc").StartType);
        if (restored != original)
        {
            problems.Add("undo did not restore start mode");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static string TestTaskDisable()
    {
        var problems = new List<string>();
        using var taskService = new TS.TaskService();
        const string fullPath = @"\Microsoft\Windows\Autochk\Proxy";
        var task = taskService.GetTask(fullPath);
        if (task == null)
        {
            return "SKIP: test task not present";
        }
        if (!task.Definition.Settings.Enabled)
        {
            return "SKIP: already disabled";
        }

        var engine = new FixEngine();
        var result = engine.ApplyAsync(new[]
        {
            Fix("Test", "task", new ScheduledTaskAction(@"\Microsoft\Windows\Autochk\", "Proxy"))
        }).GetAwaiter().GetResult();
        if (result.Applied != 1)
        {
            problems.Add("apply status: " + result.Applied);
        }

        var after = new TS.TaskService().GetTask(fullPath);
        if (after == null || after.Definition.Settings.Enabled)
        {
            problems.Add("task not disabled");
        }

        engine.SaveSessionBackup();
        UndoLatest();
        var restored = new TS.TaskService().GetTask(fullPath);
        if (restored == null || !restored.Definition.Settings.Enabled)
        {
            problems.Add("undo did not re-enable task");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", problems));
        }
        return "";
    }

    private static void DeleteKeyTree(string path)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software", true);
            key?.DeleteSubKeyTree("OpenWindowsSelfTest", false);
        }
        catch
        {
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
