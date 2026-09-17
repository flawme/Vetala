using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vetala.Models;
using Vetala.Services;

namespace Vetala.ViewModels;

public partial class SidebarViewModel : ViewModelBase
{
    private readonly FileSystemService _fileSystemService;
    private string _currentRootPath = string.Empty;
    private CancellationTokenSource? _refreshCts;

    public ObservableCollection<FileSystemNode> RootNodes { get; } = new();
    public ObservableCollection<FlatTreeItem> FlatNodes { get; } = new();

    public IEnumerable<string> AllFiles => GetAllFilePaths(RootNodes);

    private IEnumerable<string> GetAllFilePaths(IEnumerable<FileSystemNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory)
                yield return node.FullPath;
            foreach (var child in GetAllFilePaths(node.Children))
                yield return child;
        }
    }

    [ObservableProperty]
    private FlatTreeItem? _selectedItem;

    public Action<string>? OnFileOpened { get; set; }

    public SidebarViewModel()
    {
        _fileSystemService = new FileSystemService();
        _fileSystemService.FileSystemChanged += OnFileSystemChanged;
    }

    public void LoadProject(string rootPath)
    {
        _currentRootPath = rootPath;
        _fileSystemService.Initialize(rootPath);
        RefreshTree();
    }

    private void OnFileSystemChanged(object? sender, EventArgs e)
    {
        RefreshTree();
    }

    private void RefreshTree()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;

        var expandedPaths = GetExpandedPaths(RootNodes).ToHashSet();
        var selectedPath = SelectedItem?.Node.FullPath;

        RootNodes.Clear();
        if (string.IsNullOrEmpty(_currentRootPath) || !Directory.Exists(_currentRootPath)) return;

        var rootNode = new FileSystemNode(_currentRootPath, true) { IsExpanded = true };
        RootNodes.Add(rootNode);

        PopulateChildren(rootNode, expandedPaths, token);
        BuildFlatList();

        if (selectedPath != null)
        {
            FlatTreeItem? restore = null;
            foreach (var item in FlatNodes)
            {
                if (item.Node.FullPath == selectedPath)
                {
                    restore = item;
                    break;
                }
            }
            if (restore != null)
                SelectedItem = restore;
        }
    }

    private void PopulateChildren(FileSystemNode node, HashSet<string> expandedPaths, CancellationToken token = default)
    {
        if (!node.IsDirectory || token.IsCancellationRequested) return;

        node.Children.Clear();
        var children = _fileSystemService.GetDirectoryNodes(node.FullPath);
        foreach (var child in children)
        {
            if (token.IsCancellationRequested) return;
            if (child.IsDirectory)
            {
                child.IsExpandable = true;
                child.Children.Add(new FileSystemNode("dummy", false));
            }
            node.Children.Add(child);

            if (child.IsDirectory && expandedPaths.Contains(child.FullPath))
            {
                child.IsExpanded = true;
                PopulateChildren(child, expandedPaths, token);
            }
        }
    }

    private void BuildFlatList()
    {
        FlatNodes.Clear();
        foreach (var root in RootNodes)
        {
            AddToFlatList(root, 0);
        }
    }

    private void AddToFlatList(FileSystemNode node, int depth)
    {
        FlatNodes.Add(new FlatTreeItem(node, depth));
        if (node.IsDirectory && node.IsExpanded)
        {
            foreach (var child in node.Children)
            {
                AddToFlatList(child, depth + 1);
            }
        }
    }

    private IEnumerable<string> GetExpandedPaths(IEnumerable<FileSystemNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded)
            {
                yield return node.FullPath;
                foreach (var childPath in GetExpandedPaths(node.Children))
                {
                    yield return childPath;
                }
            }
        }
    }

    [RelayCommand]
    private void ToggleExpand(FlatTreeItem? item)
    {
        if (item == null || !item.IsDirectory) return;

        item.Node.IsExpanded = !item.Node.IsExpanded;

        if (item.Node.IsExpanded)
        {
            var expandedPaths = new HashSet<string> { item.Node.FullPath };
            var needPopulate = item.Node.Children.Count == 1 &&
                               item.Node.Children[0].FullPath == "dummy";
            if (needPopulate)
            {
                PopulateChildren(item.Node, expandedPaths);
            }
        }

        BuildFlatList();

        var newItem = FlatNodes.FirstOrDefault(f => f.Node == item.Node);
        if (newItem != null)
            SelectedItem = newItem;
    }

    [RelayCommand]
    private void OpenFileItem(FlatTreeItem? item)
    {
        if (item == null || item.IsDirectory) return;
        OnFileOpened?.Invoke(item.Node.FullPath);
    }

    [RelayCommand]
    private async Task NewFile(FileSystemNode? node)
    {
        string parentPath = GetTargetParentPath(node);
        string? fileName = await DialogService.ShowInputDialogAsync("New File", "Enter file name:");

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                _fileSystemService.CreateFile(parentPath, fileName);
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialogAsync("Error", $"Could not create file:\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task NewFolder(FileSystemNode? node)
    {
        string parentPath = GetTargetParentPath(node);
        string? folderName = await DialogService.ShowInputDialogAsync("New Folder", "Enter folder name:");

        if (!string.IsNullOrWhiteSpace(folderName))
        {
            try
            {
                _fileSystemService.CreateFolder(parentPath, folderName);
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialogAsync("Error", $"Could not create folder:\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task Rename(FileSystemNode? node)
    {
        if (node == null) return;

        string? newName = await DialogService.ShowInputDialogAsync("Rename", "Enter new name:", node.Name);

        if (!string.IsNullOrWhiteSpace(newName) && newName != node.Name)
        {
            try
            {
                _fileSystemService.RenameItem(node.FullPath, newName);
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialogAsync("Error", $"Could not rename:\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task Delete(FileSystemNode? node)
    {
        if (node == null) return;

        bool confirm = await DialogService.ShowConfirmDialogAsync("Confirm Delete",
            $"Are you sure you want to delete '{node.Name}'? This action cannot be undone.");

        if (confirm)
        {
            try
            {
                _fileSystemService.DeleteItem(node.FullPath);
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialogAsync("Error", $"Could not delete:\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void Duplicate(FileSystemNode? node)
    {
        if (node != null)
        {
            _fileSystemService.DuplicateItem(node.FullPath);
        }
    }

    [RelayCommand]
    private async Task CopyPath(FileSystemNode? node)
    {
        if (node != null)
        {
            await DialogService.ShowConfirmDialogAsync("Copy Path", node.FullPath);
        }
    }

    [RelayCommand]
    private void CommitRename(FlatTreeItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.RenameText)) return;

        string newName = item.RenameText.Trim();
        if (newName == item.Name) { item.CancelRename(); return; }

        try
        {
            _fileSystemService.RenameItem(item.Node.FullPath, newName);
            item.CancelRename();
            RefreshTree();
        }
        catch
        {
            item.CancelRename();
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var root in RootNodes)
        {
            CollapseNode(root);
        }
        BuildFlatList();
    }

    private void CollapseNode(FileSystemNode node)
    {
        node.IsExpanded = false;
        foreach (var child in node.Children)
        {
            CollapseNode(child);
        }
    }

    private string GetTargetParentPath(FileSystemNode? node)
    {
        if (node == null) return _currentRootPath;
        if (node.IsDirectory) return node.FullPath;
        return Path.GetDirectoryName(node.FullPath) ?? _currentRootPath;
    }

    public FileSystemNode? GetNodeFromFlat(FlatTreeItem? item) => item?.Node;
}
