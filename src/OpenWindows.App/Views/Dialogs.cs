using OpenWindows.Models;
using WpfMessageBox = Wpf.Ui.Controls.MessageBox;
using WpfMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace OpenWindows.Views;

public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        var box = new WpfMessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No"
        };
        var result = await box.ShowDialogAsync();
        return result == WpfMessageBoxResult.Primary;
    }

    public static async Task ShowAsync(string title, string message)
    {
        var box = new WpfMessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await box.ShowDialogAsync();
    }

    public static async Task<IReadOnlySet<(int Id, string Name)>?> ConfirmKillsAsync(
        IReadOnlyList<KillProcessAction> kills)
    {
        var list = string.Join(Environment.NewLine, kills.Select(k => $"- {k.Name} (PID {k.Id})"));
        var box = new WpfMessageBox
        {
            Title = "End processes",
            Content = $"End these {kills.Count} process(es)? Unsaved work in them will be lost.{Environment.NewLine}{Environment.NewLine}{list}",
            PrimaryButtonText = "End them",
            CloseButtonText = "Skip"
        };
        var result = await box.ShowDialogAsync();
        return result == WpfMessageBoxResult.Primary
            ? kills.Select(k => (k.Id, k.Name)).ToHashSet()
            : new HashSet<(int Id, string Name)>();
    }
}
