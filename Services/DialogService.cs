using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Vetala.Views;

namespace Vetala.Services;

public static class DialogService
{
    private static Avalonia.Controls.Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    public static async Task<string?> ShowInputDialogAsync(string title, string prompt, string initialText = "")
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var dialog = new InputDialog
        {
            DialogTitle = title,
            Prompt = prompt,
            InputText = initialText
        };

        return await dialog.ShowDialog<string?>(mainWindow);
    }

    public static async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return false;

        var dialog = new ConfirmDialog
        {
            DialogTitle = title,
            Message = message
        };

        return await dialog.ShowDialog<bool>(mainWindow);
    }
}
