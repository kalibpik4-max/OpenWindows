namespace OpenWindows.Services;

public static class AppPaths
{
    public static string AppDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWindows");

    public static string? BackupDirOverride { get; set; }

    public static string BackupDir => BackupDirOverride ?? Path.Combine(AppDir, "backups");

    public static string LogFile => Path.Combine(AppDir, "optimizer.log");

    public static string SettingsFile => Path.Combine(AppDir, "settings.json");

    public static string DisabledStartupDir => Path.Combine(AppDir, "disabled-startup");

    public static void EnsureAppDir() => Directory.CreateDirectory(AppDir);

    public static void EnsureBackupDir() => Directory.CreateDirectory(BackupDir);
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureAppDir();
                File.AppendAllText(AppPaths.LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
