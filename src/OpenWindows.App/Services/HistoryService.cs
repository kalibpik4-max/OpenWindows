using System.Text.Json;
using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed record BackupSessionInfo(
    string FilePath,
    string FileName,
    DateTime Modified,
    int EntryCount,
    bool Reverted,
    IReadOnlyList<UndoEntry> Entries);

public sealed class HistoryService
{
    public static string? FindLatestBackupFile()
    {
        if (!Directory.Exists(AppPaths.BackupDir))
        {
            return null;
        }
        return Directory.EnumerateFiles(AppPaths.BackupDir, "backup-*.json")
            .Where(f => !f.EndsWith("-reverted.json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    public List<BackupSessionInfo> ListSessions()
    {
        var sessions = new List<BackupSessionInfo>();
        if (!Directory.Exists(AppPaths.BackupDir))
        {
            return sessions;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.BackupDir, "backup-*.json"))
        {
            try
            {
                var reverted = file.EndsWith("-reverted.json", StringComparison.OrdinalIgnoreCase);
                List<UndoEntry> entries;
                try
                {
                    var json = File.ReadAllText(file);
                    entries = JsonSerializer.Deserialize<List<UndoEntry>>(json) ?? new List<UndoEntry>();
                }
                catch
                {
                    entries = new List<UndoEntry>();
                }

                sessions.Add(new BackupSessionInfo(
                    file,
                    Path.GetFileName(file),
                    File.GetLastWriteTime(file),
                    entries.Count,
                    reverted,
                    entries));
            }
            catch
            {
            }
        }

        return sessions.OrderByDescending(s => s.Modified).ToList();
    }
}
