using OpenWindows.Models;
using OpenWindows.Services;

namespace OpenWindows.ViewModels;

public sealed class FindingItemVM : ViewModelBase
{
    private bool isSelected;

    public FindingItemVM(Finding model)
    {
        Model = model;
        isSelected = model.DefaultChecked;
    }

    public Finding Model { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public string Title => Model.Title + (Model.NeedsAdmin && !ElevationHelper.IsAdmin ? "  [admin]" : "");

    public string Detail => Model.Detail;

    public string CategoryLabel => Model.CategoryLabel;

    public string RiskLabel => Model.RiskLabel;

    public bool IsSafe => Model.Risk == RiskLevel.Safe;

    public bool IsCaution => Model.Risk == RiskLevel.Caution;

    public bool IsInfo => Model.Risk == RiskLevel.Info;

    public bool NeedsAdminBadge => Model.NeedsAdmin && !ElevationHelper.IsAdmin;

    public bool HasAction => Model.Action != null;
}

public sealed class ProcessRowVM
{
    public required string Name { get; init; }
    public required int Id { get; init; }
    public required double CpuPercent { get; init; }
    public required long RamMb { get; init; }
    public bool HasWindow { get; init; } = true;
    public string CpuText => CpuPercent.ToString("0.0") + "%";
    public string RamText => RamMb >= 1024
        ? (RamMb / 1024.0).ToString("0.0") + " GB"
        : RamMb + " MB";
    public string CpuLevel => CpuPercent >= 25 ? "High" : CpuPercent >= 8 ? "Medium" : "Low";
    public bool IsBackground => !HasWindow;
    public string StatusText => HasWindow ? "In use" : "Background";
    public bool CanEnd => !ProtectedProcesses.IsProtected(Name);
}
