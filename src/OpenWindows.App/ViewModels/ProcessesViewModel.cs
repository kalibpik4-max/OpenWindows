using System.ComponentModel;
using System.Windows.Threading;
using OpenWindows.Models;
using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public sealed class ProcessesViewModel : ViewModelBase
{
    private readonly DispatcherTimer timer;
    private bool isBusy;
    private string summaryText = "Loading processes...";
    private bool suppressTimer;

    public ObservableCollection<ProcessItemVM> Items { get; } = new();

    public ListCollectionView View { get; }

    public Func<IReadOnlyList<KillProcessAction>, Task<IReadOnlySet<(int Id, string Name)>?>>? ConfirmKillsAsync { get; set; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand EndSelectedCommand { get; }

    public AsyncRelayCommand EndUnusedCommand { get; }

    public AsyncRelayCommand<ProcessItemVM> EndOneCommand { get; }

    public RelayCommand SelectUnusedCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public ProcessesViewModel()
    {
        View = (ListCollectionView)CollectionViewSource.GetDefaultView(Items);
        View.SortDescriptions.Add(new SortDescription(nameof(ProcessItemVM.SortScore), ListSortDirection.Descending));
        View.IsLiveSorting = true;
        View.LiveSortingProperties.Add(nameof(ProcessItemVM.SortScore));

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        EndSelectedCommand = new AsyncRelayCommand(EndSelectedAsync, () => SelectedCount > 0);
        EndUnusedCommand = new AsyncRelayCommand(EndUnusedAsync, () => Items.Any(i => i.IsUnused && !i.IsProtected));
        EndOneCommand = new AsyncRelayCommand<ProcessItemVM>(EndOneAsync);
        SelectUnusedCommand = new RelayCommand(SelectUnused);
        ClearSelectionCommand = new RelayCommand(ClearSelection);

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            if (!suppressTimer)
            {
                _ = RefreshAsync();
            }
        };
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    public int SelectedCount => Items.Count(i => i.IsSelected && !i.IsProtected);

    public string EndSelectedText => $"End selected ({SelectedCount})";

    public void Start()
    {
        if (!timer.IsEnabled)
        {
            timer.Start();
        }
        _ = RefreshAsync();
    }

    public void Stop()
    {
        timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            var snapshot = await Task.Run(() => ProcessScanService.Snapshot());
            Reconcile(snapshot);
            UpdateSummary();
        }
        catch (Exception ex)
        {
            Log.Write("Processes refresh failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Reconcile(List<AppProcessInfo> snapshot)
    {
        var existing = Items.ToDictionary(i => i.Id);
        var seen = new HashSet<int>();

        foreach (var info in snapshot)
        {
            seen.Add(info.Id);
            if (existing.TryGetValue(info.Id, out var item) && string.Equals(item.Name, info.Name, StringComparison.OrdinalIgnoreCase))
            {
                item.Apply(info);
            }
            else
            {
                if (item != null)
                {
                    RemoveItem(item);
                }
                AddItem(new ProcessItemVM(info));
            }
        }

        foreach (var stale in Items.Where(i => !seen.Contains(i.Id)).ToList())
        {
            RemoveItem(stale);
        }
    }

    private void AddItem(ProcessItemVM item)
    {
        item.PropertyChanged += OnItemSelectionChanged;
        Items.Add(item);
    }

    private void RemoveItem(ProcessItemVM item)
    {
        item.PropertyChanged -= OnItemSelectionChanged;
        Items.Remove(item);
    }

    private void UpdateSummary()
    {
        var unused = Items.Where(i => i.IsUnused).ToList();
        var unusedRam = unused.Sum(i => i.RamMb) / 1024.0;
        SummaryText = unused.Count == 0
            ? $"{Items.Count} processes running - nothing running in the background needs attention."
            : $"{Items.Count} processes running - {unused.Count} not in use, about {unusedRam:0.0} GB reclaimable.";
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(EndSelectedText));
        EndSelectedCommand.RaiseCanExecuteChanged();
        EndUnusedCommand.RaiseCanExecuteChanged();
    }

    private void SelectUnused()
    {
        suppressTimer = true;
        foreach (var item in Items)
        {
            if (item.IsUnused && !item.IsProtected)
            {
                item.IsSelected = true;
            }
        }
        suppressTimer = false;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(EndSelectedText));
    }

    private void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(EndSelectedText));
    }

    private async Task EndSelectedAsync()
    {
        var targets = Items.Where(i => i.IsSelected && !i.IsProtected).ToList();
        if (targets.Count == 0)
        {
            return;
        }
        await KillAsync(targets);
    }

    private async Task EndUnusedAsync()
    {
        var targets = Items.Where(i => i.IsUnused).ToList();
        if (targets.Count == 0)
        {
            return;
        }
        await KillAsync(targets);
    }

    private async Task EndOneAsync(ProcessItemVM item)
    {
        if (item == null || item.IsProtected)
        {
            return;
        }
        await KillAsync(new List<ProcessItemVM> { item });
    }

    private async Task KillAsync(List<ProcessItemVM> targets)
    {
        if (ConfirmKillsAsync == null)
        {
            return;
        }

        var kills = targets.Select(t => new KillProcessAction(t.Id, t.Name)).ToList();
        var approved = await ConfirmKillsAsync(kills);
        if (approved == null || approved.Count == 0)
        {
            return;
        }

        var ended = 0;
        await Task.Run(() =>
        {
            foreach (var target in targets)
            {
                if (!approved.Contains((target.Id, target.Name)))
                {
                    continue;
                }
                if (ProcessScanService.TryKill(target.Id, target.Name, out _))
                {
                    ended++;
                }
            }
        });

        AppServices.NotifyUser("Processes ended",
            ended == 1 ? "Ended 1 process." : $"Ended {ended} processes.",
            ended > 0);
        await RefreshAsync();
    }

    private void OnItemSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessItemVM.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(EndSelectedText));
            EndSelectedCommand.RaiseCanExecuteChanged();
        }
    }
}
