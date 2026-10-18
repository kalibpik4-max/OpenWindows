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
        Loaded += (_, _) => vm.RefreshFromLastScan();
    }
}
