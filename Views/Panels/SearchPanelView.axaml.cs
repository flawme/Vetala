using Avalonia.Controls;
using Avalonia.Input;
using Vetala.Models;
using Vetala.ViewModels;

namespace Vetala.Views;

public partial class SearchPanelView : UserControl
{
    public SearchPanelView()
    {
        InitializeComponent();
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm)
        {
            vm.Search.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMatchPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: SearchMatch match } && DataContext is MainWindowViewModel vm)
        {
            _ = vm.Search.OpenResult(match);
            e.Handled = true;
        }
    }
}
