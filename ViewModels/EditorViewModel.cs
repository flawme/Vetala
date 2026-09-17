using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vetala.Models;
using Vetala.Services;

namespace Vetala.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    public ObservableCollection<EditorTab> Tabs { get; } = new();

    [ObservableProperty]
    private EditorTab? _activeTab;

    [ObservableProperty]
    private bool _wordWrap = true;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    public Action? TriggerFind;
    public Action? TriggerReplace;
    public Action? TriggerGoToLine;

    /// <summary>Set by the editor view: moves the caret of the currently attached editor surface to a line.</summary>
    public Action<int>? NavigateToLine;

    /// <summary>Line to jump to once the next editor surface attaches (used when opening files from search results).</summary>
    public int PendingNavigationLine { get; set; }

    // Caret position of the active editor surface (kept in sync by the editor view)
    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    public void RequestNavigateToLine(int line)
    {
        if (ActiveTab is { IsLoading: false, HasLoadError: false } && NavigateToLine != null)
        {
            // The editor view stores the request as pending when the surface does not match the active tab yet
            NavigateToLine(line);
        }
        else
        {
            PendingNavigationLine = line;
        }
    }

    public bool HasTabs => Tabs.Count > 0;

    public IEnumerable<string> BreadcrumbSegments
    {
        get
        {
            if (ActiveTab == null) return Enumerable.Empty<string>();
            var segments = new List<string>();
            var dir = Path.GetDirectoryName(ActiveTab.FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var parts = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var take = Math.Max(0, parts.Length - 2);
                for (int i = take; i < parts.Length; i++)
                {
                    segments.Add(parts[i]);
                }
            }
            segments.Add(ActiveTab.Title);
            return segments;
        }
    }

    public EditorViewModel()
    {
        Tabs.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasTabs));
            OnPropertyChanged(nameof(BreadcrumbSegments));
        };

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ActiveTab))
                OnPropertyChanged(nameof(BreadcrumbSegments));
        };
    }

    public async Task OpenFile(string filePath)
    {
        var existingTab = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existingTab != null)
        {
            ActiveTab = existingTab;
            return;
        }

        var tab = new EditorTab(filePath, string.Empty) { IsLoading = true };
        Tabs.Add(tab);
        ActiveTab = tab;

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                tab.IsLoading = false;
                tab.HasLoadError = true;
                tab.LoadError = "File does not exist.";
                tab.IsReadOnly = true;
                return;
            }

            if (fileInfo.Length > 100 * 1024 * 1024)
            {
                tab.IsLoading = false;
                tab.HasLoadError = true;
                tab.LoadError = "File is too large to open (>100MB).";
                tab.IsReadOnly = true;
                return;
            }

            if (IsBinaryFile(filePath))
            {
                tab.IsLoading = false;
                tab.HasLoadError = true;
                tab.LoadError = "This file is binary or uses an unsupported text encoding.";
                tab.IsReadOnly = true;
                return;
            }

            string content = await Task.Run(() => File.ReadAllText(filePath));
            tab.Document = new AvaloniaEdit.Document.TextDocument(content);
            tab.Document.TextChanged += (s, e) => tab.IsDirty = true;
            tab.UpdateSyntaxHighlighting();
            tab.IsLoading = false;
        }
        catch (Exception ex)
        {
            tab.IsLoading = false;
            tab.HasLoadError = true;
            tab.LoadError = $"Could not open file:\n{ex.Message}";
            tab.IsReadOnly = true;
        }
    }

    private bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string[] binaryExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".dll", ".exe", ".so", ".dylib", ".bin", ".zip", ".tar", ".gz", ".7z", ".pdf", ".mp4", ".mp3", ".wav", ".icns" };

        if (binaryExtensions.Contains(ext)) return true;

        try
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[8192];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0) return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    [RelayCommand]
    private async Task CloseTab(EditorTab? tab)
    {
        if (tab == null) return;

        if (tab.IsDirty)
        {
            bool confirm = await DialogService.ShowConfirmDialogAsync(
                "Unsaved Changes",
                $"'{tab.Title}' has unsaved changes. Are you sure you want to close it? Your changes will be lost.");

            if (!confirm) return;
        }

        int nextTabIndex = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (ActiveTab == tab)
        {
            ActiveTab = null;
            if (Tabs.Count > 0)
            {
                int newIndex = Math.Min(nextTabIndex, Tabs.Count - 1);
                ActiveTab = Tabs[newIndex];
            }
            OnPropertyChanged(nameof(BreadcrumbSegments));
        }
    }

    [RelayCommand]
    private async Task SaveTab(EditorTab? tab)
    {
        if (tab == null) return;

        try
        {
            await File.WriteAllTextAsync(tab.FilePath, tab.Document.Text);
            tab.IsDirty = false;
        }
        catch (Exception ex)
        {
            await DialogService.ShowConfirmDialogAsync("Save Error", $"Could not save file:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveAll()
    {
        foreach (var tab in Tabs.Where(t => t.IsDirty))
        {
            await SaveTab(tab);
        }
    }

    [RelayCommand]
    private async Task CloseAllTabs()
    {
        var dirtyTabs = Tabs.Where(t => t.IsDirty).ToList();
        if (dirtyTabs.Any())
        {
            bool confirm = await DialogService.ShowConfirmDialogAsync(
                "Unsaved Changes",
                $"You have {dirtyTabs.Count} unsaved files. Are you sure you want to close all? Changes will be lost.");

            if (!confirm) return;
        }

        Tabs.Clear();
        ActiveTab = null;
    }

    [RelayCommand]
    private async Task CloseOthersTabs(EditorTab? keepTab)
    {
        if (keepTab == null) return;

        var tabsToClose = Tabs.Where(t => t != keepTab).ToList();
        var dirtyTabs = tabsToClose.Where(t => t.IsDirty).ToList();

        if (dirtyTabs.Any())
        {
            bool confirm = await DialogService.ShowConfirmDialogAsync(
                "Unsaved Changes",
                $"You have {dirtyTabs.Count} unsaved files. Are you sure you want to close them? Changes will be lost.");

            if (!confirm) return;
        }

        foreach (var tab in tabsToClose)
        {
            Tabs.Remove(tab);
        }

        ActiveTab = keepTab;
    }
}
