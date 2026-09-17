using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vetala.ViewModels;

namespace Vetala.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var bottomPanel = this.FindControl<BottomPanelView>("BottomPanel");
        bottomPanel?.KillTerminal();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && !shift && e.Key == Key.S)
        {
            vm.Editor.SaveTabCommand.Execute(vm.Editor.ActiveTab);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.S)
        {
            vm.Editor.SaveAllCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.W)
        {
            vm.Editor.CloseTabCommand.Execute(vm.Editor.ActiveTab);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.P)
        {
            if (vm.IsProjectOpen) ShowCommandPalette();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.P)
        {
            if (vm.IsProjectOpen) ShowQuickOpen();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.K)
        {
            if (vm.IsProjectOpen) ShowQuickActions();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.B)
        {
            if (vm.IsProjectOpen) vm.ToggleExplorerCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.F)
        {
            if (vm.IsProjectOpen) vm.ToggleSearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.G)
        {
            if (vm.IsProjectOpen) vm.ToggleSourceControlCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ShowCommandPalette()
    {
        var vm = (MainWindowViewModel)DataContext!;

        var dialog = new Window
        {
            Title = "Command Palette",
            Width = 550,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse("#454545")),
            BorderThickness = new Thickness(1)
        };

        var dock = new DockPanel();

        var searchBox = new TextBox
        {
            PlaceholderText = "Type a command...",
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
            FontSize = 14,
            Margin = new Thickness(12, 12, 12, 0)
        };
        DockPanel.SetDock(searchBox, Dock.Top);

        var results = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var commands = new (string Label, string Shortcut, Action Action)[]
        {
            ("File: Save", "Ctrl+S", () => vm.Editor.SaveTabCommand.Execute(vm.Editor.ActiveTab)),
            ("File: Save All", "Ctrl+Shift+S", () => vm.Editor.SaveAllCommand.Execute(null)),
            ("File: Close Tab", "Ctrl+W", () => vm.Editor.CloseTabCommand.Execute(vm.Editor.ActiveTab)),
            ("View: Toggle Explorer", "Ctrl+B", () => vm.ToggleExplorerCommand.Execute(null)),
            ("View: Toggle Search", "Ctrl+Shift+F", () => vm.ToggleSearchCommand.Execute(null)),
            ("View: Toggle Source Control", "Ctrl+Shift+G", () => vm.ToggleSourceControlCommand.Execute(null)),
            ("View: Toggle Word Wrap", "", () => vm.Editor.WordWrap = !vm.Editor.WordWrap),
            ("View: Toggle Line Numbers", "", () => vm.Editor.ShowLineNumbers = !vm.Editor.ShowLineNumbers),
        };

        var filtered = commands;
        results.ItemsSource = filtered.Select(c => $"{c.Label}  ({c.Shortcut})").ToList();

        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text?.ToLower() ?? "";
            filtered = commands.Where(c => c.Label.ToLower().Contains(query) || c.Shortcut.ToLower().Contains(query)).ToArray();
            results.ItemsSource = filtered.Select(c => $"{c.Label}  ({c.Shortcut})").ToList();
        };

        results.DoubleTapped += (_, _) =>
        {
            if (results.SelectedIndex >= 0 && results.SelectedIndex < filtered.Length)
            {
                filtered[results.SelectedIndex].Action();
                dialog.Close();
            }
        };

        searchBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                dialog.Close();
            else if (args.Key == Key.Enter && filtered.Length > 0)
            {
                filtered[0].Action();
                dialog.Close();
            }
        };

        dock.Children.Add(searchBox);
        dock.Children.Add(results);
        dialog.Content = dock;

        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                dialog.Close();
        };

        searchBox.Focus();
        dialog.ShowDialog(this);
    }

    private void ShowQuickOpen()
    {
        var vm = (MainWindowViewModel)DataContext!;

        var dialog = new Window
        {
            Title = "Quick Open",
            Width = 550,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse("#454545")),
            BorderThickness = new Thickness(1)
        };

        var dock = new DockPanel();

        var searchBox = new TextBox
        {
            PlaceholderText = "Search files by name...",
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
            FontSize = 14,
            Margin = new Thickness(12, 12, 12, 0)
        };
        DockPanel.SetDock(searchBox, Dock.Top);

        var results = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var allFiles = vm.Sidebar.AllFiles.ToList();
        var filtered = allFiles.ToList();
        results.ItemsSource = filtered;

        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text?.ToLower() ?? "";
            filtered = allFiles.Where(f => System.IO.Path.GetFileName(f).ToLower().Contains(query)).ToList();
            results.ItemsSource = filtered;
        };

        results.DoubleTapped += (_, _) =>
        {
            if (results.SelectedItem is string file)
            {
                _ = vm.Editor.OpenFile(file);
                dialog.Close();
            }
        };

        searchBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                dialog.Close();
            else if (args.Key == Key.Enter && filtered.Count > 0)
            {
                _ = vm.Editor.OpenFile(filtered[0]);
                dialog.Close();
            }
        };

        dock.Children.Add(searchBox);
        dock.Children.Add(results);
        dialog.Content = dock;

        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                dialog.Close();
        };

        searchBox.Focus();
        dialog.ShowDialog(this);
    }

    private void ShowQuickActions()
    {
        var vm = (MainWindowViewModel)DataContext!;

        var dialog = new Window
        {
            Title = "Quick Actions",
            Width = 500,
            Height = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse("#454545")),
            BorderThickness = new Thickness(1)
        };

        var dock = new DockPanel();

        var searchBox = new TextBox
        {
            PlaceholderText = "Type a command...",
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
            FontSize = 14,
            Margin = new Thickness(12, 12, 12, 0)
        };
        DockPanel.SetDock(searchBox, Dock.Top);

        var results = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var actions = new (string Label, string Shortcut, Action Action)[]
        {
            ("File: Save", "Ctrl+S", () => vm.Editor.SaveTabCommand.Execute(vm.Editor.ActiveTab)),
            ("File: Save All", "Ctrl+Shift+S", () => vm.Editor.SaveAllCommand.Execute(null)),
            ("File: Close Tab", "Ctrl+W", () => vm.Editor.CloseTabCommand.Execute(vm.Editor.ActiveTab)),
            ("View: Toggle Explorer", "Ctrl+B", () => vm.ToggleExplorerCommand.Execute(null)),
            ("View: Toggle Search", "Ctrl+Shift+F", () => vm.ToggleSearchCommand.Execute(null)),
            ("View: Toggle Source Control", "Ctrl+Shift+G", () => vm.ToggleSourceControlCommand.Execute(null)),
            ("View: Toggle Word Wrap", "", () => vm.Editor.WordWrap = !vm.Editor.WordWrap),
            ("View: Toggle Line Numbers", "", () => vm.Editor.ShowLineNumbers = !vm.Editor.ShowLineNumbers),
            ("View: Find", "Ctrl+F", () => vm.Editor.TriggerFind?.Invoke()),
            ("View: Replace", "Ctrl+H", () => vm.Editor.TriggerReplace?.Invoke()),
            ("View: Go to Line", "Ctrl+G", () => vm.Editor.TriggerGoToLine?.Invoke()),
        };

        var filtered = actions;
        results.ItemsSource = filtered.Select(c => $"{c.Label}  ({c.Shortcut})").ToList();

        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text?.ToLower() ?? "";
            filtered = actions.Where(c => c.Label.ToLower().Contains(query) || c.Shortcut.ToLower().Contains(query)).ToArray();
            results.ItemsSource = filtered.Select(c => $"{c.Label}  ({c.Shortcut})").ToList();
        };

        results.DoubleTapped += (_, _) =>
        {
            if (results.SelectedIndex >= 0 && results.SelectedIndex < filtered.Length)
            {
                filtered[results.SelectedIndex].Action();
                dialog.Close();
            }
        };

        searchBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                dialog.Close();
            else if (args.Key == Key.Enter && filtered.Length > 0)
            {
                filtered[0].Action();
                dialog.Close();
            }
        };

        dock.Children.Add(searchBox);
        dock.Children.Add(results);
        dialog.Content = dock;

        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                dialog.Close();
        };

        searchBox.Focus();
        dialog.ShowDialog(this);
    }
}
