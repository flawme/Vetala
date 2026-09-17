using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vetala.ViewModels;

namespace Vetala.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        await PickFolder();
    }

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        await PickFolder();
    }

    private void OnDocumentationClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/flawme/Vetala");
    }

    private void OnReportIssueClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/flawme/Vetala/issues");
    }

    private void OnFeaturesClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/flawme/Vetala/blob/master/FEATURES.md");
    }

    private void OnThirdPartyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NavigateToCommand.Execute("Licenses");
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async Task PickFolder()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Project Folder",
            AllowMultiple = false
        });

        if (result.Count >= 1)
        {
            var folderPath = result[0].Path.LocalPath;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RequestPermission(folderPath);
            }
        }
    }
}
