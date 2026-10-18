using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public sealed class ProcessItemVM : ViewModelBase
{
    private bool isSelected;
    private double cpuPercent;
    private long ramMb;
    private string title = "";
    private bool hasWindow;
    private bool isUnused;

    public ProcessItemVM(AppProcessInfo info)
    {
        Id = info.Id;
        Name = info.Name;
        IsProtected = info.IsProtected;
        Apply(info);
    }

    public int Id { get; }

    public string Name { get; }

    public bool IsProtected { get; }

    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    public bool HasWindow
    {
        get => hasWindow;
        private set
        {
            if (SetProperty(ref hasWindow, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsUnused
    {
        get => isUnused;
        private set
        {
            if (SetProperty(ref isUnused, value))
            {
                OnPropertyChanged(nameof(SortScore));
            }
        }
    }

    public double CpuPercent
    {
        get => cpuPercent;
        private set
        {
            if (SetProperty(ref cpuPercent, value))
            {
                OnPropertyChanged(nameof(CpuText));
                OnPropertyChanged(nameof(CpuLevel));
                OnPropertyChanged(nameof(SortScore));
            }
        }
    }

    public string CpuLevel => CpuPercent >= 25 ? "High" : CpuPercent >= 8 ? "Medium" : "Low";

    public bool CanSelect => !IsProtected;

    public long RamMb
    {
        get => ramMb;
        private set
        {
            if (SetProperty(ref ramMb, value))
            {
                OnPropertyChanged(nameof(RamText));
                OnPropertyChanged(nameof(SortScore));
            }
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (IsProtected)
            {
                value = false;
            }
            SetProperty(ref isSelected, value);
        }
    }

    public string CpuText => CpuPercent < 0.1 ? "0%" : CpuPercent.ToString("0.0") + "%";

    public string RamText => RamMb >= 1024
        ? (RamMb / 1024.0).ToString("0.0") + " GB"
        : RamMb + " MB";

    public string StatusText => IsProtected ? "Protected" : HasWindow ? "In use" : "Background";

    public double SortScore => (IsUnused ? 1_000_000 : 0) + CpuPercent * 1000 + RamMb;

    public void Apply(AppProcessInfo info)
    {
        Title = info.Title;
        HasWindow = info.HasWindow;
        IsUnused = ProcessScanService.IsUnused(info);
        CpuPercent = info.CpuPercent;
        RamMb = info.RamMb;
        if (IsProtected)
        {
            IsSelected = false;
        }
    }
}
