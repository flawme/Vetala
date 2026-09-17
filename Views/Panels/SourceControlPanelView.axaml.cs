using Avalonia.Controls;
using Avalonia.Input;
using Vetala.Models;
using Vetala.ViewModels;

namespace Vetala.Views;

public partial class SourceControlPanelView : UserControl
{
    public SourceControlPanelView()
    {
        InitializeComponent();
    }

    private void OnFilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: GitStatusEntry entry } && DataContext is MainWindowViewModel vm)
        {
            vm.SourceControl.OpenFile(entry);
            e.Handled = true;
        }
    }
}
