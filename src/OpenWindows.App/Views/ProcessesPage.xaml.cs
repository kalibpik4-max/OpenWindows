using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    private void ProcessList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }
        var item = FindAncestor<ListViewItem>(source);
        if (item?.DataContext is not ProcessItemVM row || !row.CanSelect)
        {
            return;
        }
        if (FindAncestor<ButtonBase>(source) != null || FindAncestor<ScrollBar>(source) != null)
        {
            return;
        }
        row.IsSelected = !row.IsSelected;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
