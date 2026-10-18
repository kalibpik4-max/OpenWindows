using System.Windows;
using OpenWindows.Services;
using OpenWindows.ViewModels;
using OpenWindows.Views;
using Wpf.Ui.Controls;

namespace OpenWindows;

public partial class MainWindow : FluentWindow
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        AppServices.Snackbars.SetSnackbarPresenter(SnackbarPresenter);
        AppServices.Notifications.SetFallback((title, body) =>
            Dispatcher.BeginInvoke(() => TrayIcon.ShowNotification(title, body)));
        AppServices.Notifications.Activated += (_, _) => Dispatcher.BeginInvoke(ShowFromTray);

        ThemeService.Apply(AppServices.Settings.Current.Theme, this);

        TrayIcon.TrayLeftMouseUp += (_, _) => ShowFromTray();
        StateChanged += OnStateChanged;

        if (AppServices.Settings.Current.MonitoringAutoStart)
        {
            AppServices.Monitor.Start();
        }

        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(
                new Action(() => NavigateTo(AppPage.Dashboard)),
                System.Windows.Threading.DispatcherPriority.Background);
    }

    public void NavigateTo(AppPage page)
    {
        try
        {
            RootNavigation.ApplyTemplate();
            RootNavigation.Navigate(PageTypeFor(page));
        }
        catch (Exception ex)
        {
            Log.Write("Navigation failed: " + ex);
        }
    }

    private static Type PageTypeFor(AppPage page) => page switch
    {
        AppPage.Optimize => typeof(OptimizePage),
        AppPage.Monitor => typeof(MonitorPage),
        AppPage.History => typeof(HistoryPage),
        AppPage.Settings => typeof(SettingsPage),
        _ => typeof(DashboardPage)
    };

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && AppServices.Settings.Current.MinimizeToTray)
        {
            Hide();
        }
    }

    private void UpdateMonitorMenuText()
    {
        MenuItemMonitor.Header = AppServices.Monitor.IsRunning ? "Pause monitoring" : "Start monitoring";
    }

    private void MenuItemShow_Click(object sender, RoutedEventArgs e)
    {
        ShowFromTray();
    }

    private void MenuItemScan_Click(object sender, RoutedEventArgs e)
    {
        ShowFromTray();
        OptimizePageRequest.ScanPending = true;
        NavigateTo(AppPage.Optimize);
    }

    private void MenuItemMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Monitor.IsRunning)
        {
            AppServices.Monitor.Stop();
        }
        else
        {
            AppServices.Monitor.Start();
        }
        UpdateMonitorMenuText();
    }

    private void MenuItemExit_Click(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }
}
