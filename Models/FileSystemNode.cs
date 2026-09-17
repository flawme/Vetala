using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vetala.Models;

public partial class FileSystemNode : ObservableObject
{
    public string FullPath { get; }
    
    [ObservableProperty]
    private string _name = string.Empty;
    
    public string BaseName => IsDirectory ? Name : Path.GetFileNameWithoutExtension(Name);
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name);

    public bool IsDirectory { get; }

    [ObservableProperty]
    private bool _isExpanded;

    public bool IsExpandable { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExpandable));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(BaseName));
        OnPropertyChanged(nameof(Extension));
    }

    public ObservableCollection<FileSystemNode> Children { get; } = new();

    public FileSystemNode(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        _name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(_name))
        {
            _name = fullPath;
        }
        IsDirectory = isDirectory;
    }
}
