using System.Windows.Controls;
using OpenWindows.ViewModels;

namespace OpenWindows.Views;

public partial class ProcessesPage : Page
{
    private readonly ProcessesViewModel vm = new();

    public ProcessesPage()
    {
        DataContext = vm;
        InitializeComponent();
        vm.ConfirmKillsAsync = Dialogs.ConfirmKillsAsync;
        Loaded += (_, _) => vm.Start();
        Unloaded += (_, _) => vm.Stop();
    }
}
