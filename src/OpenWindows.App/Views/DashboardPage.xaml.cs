using System.Windows.Controls;
using OpenWindows.ViewModels;

namespace OpenWindows.Views;

public partial class DashboardPage : Page
{
    private readonly DashboardViewModel vm = new();

    public DashboardPage()
    {
        DataContext = vm;
        InitializeComponent();
        vm.NavigateRequested += page => MainWindow.Instance?.NavigateTo(page);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                vm.RefreshFromLastScan();
                vm.SyncMonitoringState();
                _ = vm.RefreshUnusedAsync(true);
            }
        };
    }
}
