using System.Windows.Controls;
using OpenWindows.ViewModels;

namespace OpenWindows.Views;

public partial class OptimizePage : Page
{
    private readonly OptimizeViewModel vm = new();

    public OptimizePage()
    {
        DataContext = vm;
        InitializeComponent();
        vm.ConfirmAsync = Dialogs.ConfirmAsync;
        vm.ConfirmKillsAsync = Dialogs.ConfirmKillsAsync;
        vm.ShowMessageAsync = Dialogs.ShowAsync;
        Loaded += (_, _) =>
        {
            if (OptimizePageRequest.ScanPending)
            {
                OptimizePageRequest.ScanPending = false;
                _ = vm.ScanAsync();
            }
        };
    }
}
