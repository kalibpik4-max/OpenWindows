using System.Collections.ObjectModel;
using System.Windows;
using OpenWindows.Models;
using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public enum AppPage
{
    Dashboard,
    Optimize,
    Monitor,
    Processes,
    History,
    Settings
}

public sealed class OptimizeViewModel : ViewModelBase
{
    private readonly FixEngine fixer = new();
    private CancellationTokenSource? scanCancellation;

    private bool isScanning;
    private bool isApplying;
    private string scanStatus = "Ready. Click \"Scan my PC\" to look for startup bloat, services, tweaks and cleanup. Not running as admin items are marked.";
    private double scanProgress;
    private string resultText = "";

    public ObservableCollection<FindingItemVM> Findings { get; } = new();

    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public Func<IReadOnlyList<KillProcessAction>, Task<IReadOnlySet<(int Id, string Name)>?>>? ConfirmKillsAsync { get; set; }

    public Func<string, string, Task>? ShowMessageAsync { get; set; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand CheckSafeCommand { get; }

    public RelayCommand UncheckAllCommand { get; }

    public RelayCommand RestartAsAdminCommand { get; }

    public AsyncRelayCommand ApplyCommand { get; }

    public AsyncRelayCommand UndoCommand { get; }

    public OptimizeViewModel()
    {
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && !IsApplying);
        CheckSafeCommand = new RelayCommand(CheckSafe, () => Findings.Count > 0);
        UncheckAllCommand = new RelayCommand(UncheckAll, () => Findings.Count > 0);
        RestartAsAdminCommand = new RelayCommand(RestartAsAdmin);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => SelectedActionableCount > 0 && !IsScanning && !IsApplying);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => !IsApplying && !IsScanning);
    }

    public bool IsScanning
    {
        get => isScanning;
        private set
        {
            if (SetProperty(ref isScanning, value))
            {
                OnPropertyChanged(nameof(IsProgressVisible));
                RaiseCommandStates();
            }
        }
    }

    public bool IsApplying
    {
        get => isApplying;
        private set
        {
            if (SetProperty(ref isApplying, value))
            {
                OnPropertyChanged(nameof(IsProgressVisible));
                RaiseCommandStates();
            }
        }
    }

    public bool IsProgressVisible => IsScanning || IsApplying;

    public string ScanStatus
    {
        get => scanStatus;
        private set => SetProperty(ref scanStatus, value);
    }

    public double ScanProgress
    {
        get => scanProgress;
        private set => SetProperty(ref scanProgress, value);
    }

    public string ResultText
    {
        get => resultText;
        private set => SetProperty(ref resultText, value);
    }

    public bool IsAdmin => ElevationHelper.IsAdmin;

    public string AdminStatusText => IsAdmin
        ? "Running as administrator - all fixes are available."
        : "Not running as administrator - some fixes are skipped. Use \"Restart as admin\" to unlock everything.";

    public string ApplyButtonText => $"Apply selected fixes ({SelectedActionableCount})";

    public int SelectedActionableCount => Findings.Count(f => f.IsSelected && f.HasAction);

    public async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        Findings.Clear();
        RefreshSelectionCounts();
        ResultText = "";
        ScanProgress = 0;
        ScanStatus = "Scanning...";

        scanCancellation = new CancellationTokenSource();
        var progress = new Progress<ScanCoordinator.ScanProgressInfo>(p =>
        {
            ScanStatus = $"Scanning: {p.StepName}...";
            ScanProgress = p.StepIndex * 100.0 / p.StepCount;
        });

        try
        {
            var findings = await AppServices.Scanner.ScanAllAsync(progress, scanCancellation.Token);
            foreach (var finding in findings)
            {
                var item = new FindingItemVM(finding);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FindingItemVM.IsSelected))
                    {
                        RefreshSelectionCounts();
                    }
                };
                Findings.Add(item);
            }
            ScanStatus =
                $"Scan complete: {Findings.Count} findings. Checked items get applied; \"Check safe only\" resets to the safest set.";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ScanStatus = "Scan failed: " + ex.Message;
            Log.Write("Scan failed: " + ex);
        }
        finally
        {
            IsScanning = false;
            scanCancellation?.Dispose();
            scanCancellation = null;
            RefreshSelectionCounts();
        }
    }

    private void CheckSafe()
    {
        foreach (var finding in Findings)
        {
            if (finding.HasAction)
            {
                finding.IsSelected = finding.Model.Risk == RiskLevel.Safe && finding.Model.DefaultChecked;
            }
        }
        RefreshSelectionCounts();
    }

    private void UncheckAll()
    {
        foreach (var finding in Findings)
        {
            finding.IsSelected = false;
        }
        RefreshSelectionCounts();
    }

    private static void RestartAsAdmin()
    {
        if (ElevationHelper.RelaunchElevated())
        {
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }

    private async Task ApplyAsync()
    {
        var selected = Findings.Where(f => f.IsSelected && f.Model.Action != null).ToList();
        if (selected.Count == 0)
        {
            if (ShowMessageAsync != null)
            {
                await ShowMessageAsync("Nothing selected",
                    "No fixes are checked. Review the list and check the items you want to apply.");
            }
            return;
        }

        if (ConfirmAsync != null)
        {
            var confirmed = await ConfirmAsync("Apply fixes",
                $"Apply {selected.Count} selected fix(es)? A backup is saved first so most changes can be undone. Ending processes, browser caches and the Recycle Bin will ask for confirmation.");
            if (!confirmed)
            {
                return;
            }
        }

        IReadOnlySet<(int Id, string Name)>? approvedKills = null;
        var kills = selected.Select(s => s.Model.Action).OfType<KillProcessAction>().ToList();
        if (kills.Count > 0)
        {
            approvedKills = ConfirmKillsAsync != null
                ? await ConfirmKillsAsync(kills)
                : new HashSet<(int, string)>();
        }

        IsApplying = true;
        ScanProgress = 0;
        var progress = new Progress<FixProgress>(p =>
        {
            ScanStatus = $"Applying {p.Index}/{p.Total}: {p.Title}...";
            ScanProgress = p.Index * 100.0 / p.Total;
        });

        try
        {
            var result = await fixer.ApplyAsync(
                selected.Select(s => s.Model).ToList(), approvedKills, progress);

            foreach (var applied in result.Outcomes.Where(o => o.Status == "Applied"))
            {
                var item = Findings.FirstOrDefault(f => ReferenceEquals(f.Model, applied.Finding));
                if (item != null)
                {
                    Findings.Remove(item);
                }
            }

            ResultText = result.Summary;
            ScanStatus = result.Summary;
            AppServices.Notifications.Show("OpenWindows",
                result.Applied > 0
                    ? $"Applied {result.Applied} fix(es). Undo is available if anything feels off."
                    : "No fixes were applied.");
        }
        catch (Exception ex)
        {
            ScanStatus = "Apply failed: " + ex.Message;
            Log.Write("Apply failed: " + ex);
        }
        finally
        {
            IsApplying = false;
            RefreshSelectionCounts();
        }
    }

    private async Task UndoAsync()
    {
        if (ConfirmAsync != null)
        {
            var confirmed = await ConfirmAsync("Undo last changes",
                "Undo the most recent apply session? This restores registry values, service startup types, scheduled tasks, startup shortcuts, and the power plan.");
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            var result = await Task.Run(() => fixer.UndoLatest());
            if (result.Note != null)
            {
                ResultText = result.Note;
                if (ShowMessageAsync != null)
                {
                    await ShowMessageAsync("Undo", result.Note);
                }
            }
            else
            {
                var message = $"Restored {result.Count} change(s), {result.Failed} failed. Backup file: {result.FileName}";
                ResultText = message;
                AppServices.Notifications.Show("Changes undone", $"Restored {result.Count} change(s) from {result.FileName}.");
            }
        }
        catch (Exception ex)
        {
            ResultText = "Undo failed: " + ex.Message;
            Log.Write("Undo failed: " + ex);
        }
    }

    private void RefreshSelectionCounts()
    {
        OnPropertyChanged(nameof(SelectedActionableCount));
        OnPropertyChanged(nameof(ApplyButtonText));
        ApplyCommand.RaiseCanExecuteChanged();
        CheckSafeCommand.RaiseCanExecuteChanged();
        UncheckAllCommand.RaiseCanExecuteChanged();
    }

    private void RaiseCommandStates()
    {
        ScanCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
    }
}
