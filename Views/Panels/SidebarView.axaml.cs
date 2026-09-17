using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vetala.Models;
using Vetala.ViewModels;

namespace Vetala.Views;

public partial class SidebarView : UserControl
{
    private FlatTreeItem? _renamingItem;
    private FlatTreeItem? _pendingOpenItem;
    private Timer? _clickTimer;

    public SidebarView()
    {
        InitializeComponent();
        
        var listBox = this.FindControl<ListBox>("FlatTreeList");
        listBox?.AddHandler(InputElement.KeyDownEvent, OnListBoxKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not FlatTreeItem item) return;

        var point = e.GetCurrentPoint(border);
        var vm = DataContext as MainWindowViewModel;

        // Click on expand arrow → toggle expand
        if (item.IsExpandable && point.Position.X < 16)
        {
            e.Handled = true;
            vm?.Sidebar.ToggleExpandCommand.Execute(item);
            return;
        }

        // Double click → Open file or toggle folder
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            if (item.IsDirectory)
            {
                vm?.Sidebar.ToggleExpandCommand.Execute(item);
            }
            else
            {
                vm?.Sidebar.OpenFileItemCommand.Execute(item);
            }
            return;
        }
    }

    private void StartRename(FlatTreeItem item)
    {
        _renamingItem?.CancelRename();

        _renamingItem = item;
        item.StartRename();

        var listBoxItem = FlatTreeList.ContainerFromItem(item) as ListBoxItem;
        if (listBoxItem != null)
        {
            var renameBox = FindRenameBox(listBoxItem);
            if (renameBox != null)
            {
                renameBox.Focus();
                renameBox.SelectAll();
            }
        }
    }

    private TextBox? FindRenameBox(Visual parent)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is TextBox tb && tb.Name == "RenameBox")
                return tb;
            var result = FindRenameBox(child);
            if (result != null) return result;
        }
        return null;
    }

    private void OnListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return; // Don't intercept delete key while renaming

        if (DataContext is MainWindowViewModel vm)
        {
            if (e.Key == Key.Delete) // Covers both Delete and Shift+Delete
            {
                if (vm.Sidebar.SelectedItem?.Node != null)
                {
                    vm.Sidebar.DeleteCommand.Execute(vm.Sidebar.SelectedItem.Node);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F2)
            {
                if (vm.Sidebar.SelectedItem != null)
                {
                    StartRename(vm.Sidebar.SelectedItem);
                    e.Handled = true;
                }
            }
        }
    }

    private void OnListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visual)
        {
            var item = visual.FindAncestorOfType<ListBoxItem>(true);
            if (item == null)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.Sidebar.SelectedItem = null;
                }
            }
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        if (e.Key == Key.Enter)
        {
            CommitRename(textBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            CommitRename(textBox);
    }

    private void CommitRename(TextBox textBox)
    {
        if (_renamingItem == null) return;

        var vm = DataContext as MainWindowViewModel;
        _renamingItem.RenameText = textBox.Text ?? string.Empty;
        vm?.Sidebar.CommitRenameCommand.Execute(_renamingItem);
        _renamingItem = null;
    }

    private void CancelRename()
    {
        _renamingItem?.CancelRename();
        _renamingItem = null;
    }

    private void OnItemContextRequest(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not FlatTreeItem item) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var flyout = new MenuFlyout();

        if (item.IsDirectory)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "New File",
                Command = vm.Sidebar.NewFileCommand,
                CommandParameter = item.Node
            });
            flyout.Items.Add(new MenuItem
            {
                Header = "New Folder",
                Command = vm.Sidebar.NewFolderCommand,
                CommandParameter = item.Node
            });
            flyout.Items.Add(new Separator());
        }

        flyout.Items.Add(new MenuItem
        {
            Header = "Rename",
            Command = vm.Sidebar.RenameCommand,
            CommandParameter = item.Node
        });
        flyout.Items.Add(new MenuItem
        {
            Header = "Duplicate",
            Command = vm.Sidebar.DuplicateCommand,
            CommandParameter = item.Node
        });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem
        {
            Header = "Copy Path",
            Command = vm.Sidebar.CopyPathCommand,
            CommandParameter = item.Node
        });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem
        {
            Header = "Delete",
            Foreground = Avalonia.Media.Brushes.LightCoral,
            Command = vm.Sidebar.DeleteCommand,
            CommandParameter = item.Node
        });

        flyout.ShowAt(border, true);
        e.Handled = true;
    }

    private void OnEmptyAreaContextRequest(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var flyout = new MenuFlyout
        {
            Items =
            {
                new MenuItem
                {
                    Header = "New File",
                    Command = vm.Sidebar.NewFileCommand,
                    CommandParameter = vm.Sidebar.SelectedItem?.Node
                },
                new MenuItem
                {
                    Header = "New Folder",
                    Command = vm.Sidebar.NewFolderCommand,
                    CommandParameter = vm.Sidebar.SelectedItem?.Node
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Collapse All",
                    Command = vm.Sidebar.CollapseAllCommand
                }
            }
        };

        if (sender is Control control)
            flyout.ShowAt(control, true);
        e.Handled = true;
    }
}
