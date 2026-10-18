using System.Collections.ObjectModel;
using OpenWindows.Models;
using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public sealed class BackupEntryVM
{
    public required string Description { get; init; }
}

public sealed class BackupSessionVM
{
    private readonly BackupSessionInfo info;

    public BackupSessionVM(BackupSessionInfo info)
    {
        this.info = info;
        Entries = info.Entries.Select(e => new BackupEntryVM { Description = DescribeEntry(e) }).ToList();
    }

    public string FileName => info.FileName;

    public string DateText => info.Modified.ToString("g");

    public string SummaryText => info.EntryCount == 1 ? "1 change" : $"{info.EntryCount} changes";

    public bool IsReverted => info.Reverted;

    public string StatusText => info.Reverted ? "Reverted" : "Active";

    public string StatusDetail => info.Reverted
        ? "This session was already undone."
        : "Undo restores everything in this session.";

    public IReadOnlyList<BackupEntryVM> Entries { get; }

    private static string DescribeEntry(UndoEntry entry) => entry.Type switch
    {
        "RegSet" => $"Set registry value {entry.Hive}\\{entry.SubKey}\\{entry.Name}" + (entry.OldExists == true ? " (was changed before)" : " (did not exist before)"),
        "RegRemoved" => $"Disabled auto-start entry \"{entry.Name}\"",
        "Service" => $"Changed service {entry.Name} start type (was {entry.OldStartType}" + (entry.WasRunning == true ? ", running" : ", stopped") + ")",
        "ScheduledTask" => $"Disabled scheduled task {entry.Name}",
        "FileMoved" => $"Moved startup item \"{Path.GetFileName(entry.Source ?? "")}\" out of Startup",
        "PowerPlan" => "Changed the active power plan",
        _ => entry.Type
    };
}

public sealed class HistoryViewModel : ViewModelBase
{
    private string statusText = "";

    public ObservableCollection<BackupSessionVM> Sessions { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public AsyncRelayCommand<BackupSessionVM>? UndoSessionCommand { get; }

    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public RelayCommand OpenBackupsFolderCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public HistoryViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        UndoSessionCommand = new AsyncRelayCommand<BackupSessionVM>(UndoSessionAsync);
        OpenBackupsFolderCommand = new RelayCommand(() =>
        {
            AppPaths.EnsureBackupDir();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.BackupDir)
            {
                UseShellExecute = true
            });
        });
        Refresh();
    }

    public void Refresh()
    {
        Sessions.Clear();
        foreach (var session in AppServices.History.ListSessions())
        {
            Sessions.Add(new BackupSessionVM(session));
        }
        StatusText = Sessions.Count == 0
            ? "No apply sessions yet. When you apply fixes, a backup appears here so you can undo it any time."
            : $"{Sessions.Count} session(s). Active sessions can be undone.";
    }

    private async Task UndoSessionAsync(BackupSessionVM session)
    {
        if (session.IsReverted)
        {
            return;
        }
        if (ConfirmAsync != null)
        {
            var confirmed = await ConfirmAsync("Undo session",
                $"Undo the session from {session.DateText}? Every change in this backup is restored. If most changes were applied before this session to the same items, the older backup takes precedence; undo newest-first when in doubt.");
            if (!confirmed)
            {
                return;
            }
        }

        var filePath = Path.Combine(AppPaths.BackupDir, session.FileName);
        var result = await Task.Run(() => new FixEngine().UndoFile(filePath));
        StatusText = result.Note ?? $"Restored {result.Count} change(s), {result.Failed} failed.";
        AppServices.Notifications.Show("OpenWindows", StatusText);
        Refresh();
    }
}
