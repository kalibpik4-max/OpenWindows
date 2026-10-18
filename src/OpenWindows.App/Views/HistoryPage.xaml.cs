using System.Windows.Controls;
using OpenWindows.ViewModels;

namespace OpenWindows.Views;

public partial class HistoryPage : Page
{
    private readonly HistoryViewModel vm = new();

    public HistoryPage()
    {
        DataContext = vm;
        InitializeComponent();
        vm.ConfirmAsync = Dialogs.ConfirmAsync;
        Loaded += (_, _) => vm.Refresh();
    }
}
