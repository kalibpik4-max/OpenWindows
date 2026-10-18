using System.Windows.Controls;
using OpenWindows.ViewModels;

namespace OpenWindows.Views;

public partial class MonitorPage : Page
{
    private readonly MonitorViewModel vm = new();

    public MonitorPage()
    {
        DataContext = vm;
        InitializeComponent();
        vm.ConfirmAsync = Dialogs.ConfirmAsync;
        Loaded += (_, _) => vm.SyncMonitoringState();
    }
}
