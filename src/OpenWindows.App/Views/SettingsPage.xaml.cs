using System.Windows.Controls;
using OpenWindows.ViewModels;

namespace OpenWindows.Views;

public partial class SettingsPage : Page
{
    private readonly SettingsViewModel vm = new();

    public SettingsPage()
    {
        DataContext = vm;
        InitializeComponent();
    }
}
