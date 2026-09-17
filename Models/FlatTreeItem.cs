using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vetala.Models;

public partial class FlatTreeItem : ObservableObject
{
    public FileSystemNode Node { get; }
    public int Depth { get; }
    public Thickness Indent => new(Depth * 8, 0, 0, 0);

    public string Name => Node.Name;
    public string BaseName => Node.BaseName;
    public string Extension => Node.Extension;
    public bool IsDirectory => Node.IsDirectory;
    public bool IsExpandable => Node.IsDirectory && Node.IsExpandable;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    public FlatTreeItem(FileSystemNode node, int depth)
    {
        Node = node;
        Depth = depth;
        _isExpanded = node.IsExpanded;
        _renameText = node.IsDirectory ? node.BaseName : node.Name;
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileSystemNode.IsExpanded))
                IsExpanded = node.IsExpanded;
        };
    }

    public void StartRename()
    {
        RenameText = Node.IsDirectory ? Node.BaseName : Node.Name;
        IsRenaming = true;
    }

    public void CancelRename()
    {
        IsRenaming = false;
    }
}
