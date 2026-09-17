using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Vetala.Models;
using Avalonia.Threading;

namespace Vetala.Services;

public class FileSystemService : IDisposable
{
    private string? _rootPath;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private readonly object _lock = new();
    private bool _hasPendingChange;

    public event EventHandler? FileSystemChanged;

    public void Initialize(string rootPath)
    {
        _rootPath = rootPath;
        if (_watcher != null)
        {
            _watcher.Dispose();
        }

        if (_debounceTimer == null)
        {
            _debounceTimer = new Timer(300) { AutoReset = false };
            _debounceTimer.Elapsed += OnDebounceTimerElapsed;
        }

        if (Directory.Exists(_rootPath))
        {
            _watcher = new FileSystemWatcher(_rootPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _hasPendingChange = true;
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }
    }

    private void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        bool shouldFire;
        lock (_lock)
        {
            shouldFire = _hasPendingChange;
            _hasPendingChange = false;
        }

        if (shouldFire)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FileSystemChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    public List<FileSystemNode> GetDirectoryNodes(string path)
    {
        var nodes = new List<FileSystemNode>();
        if (!Directory.Exists(path)) return nodes;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                bool isDir = (File.GetAttributes(entry) & FileAttributes.Directory) == FileAttributes.Directory;
                nodes.Add(new FileSystemNode(entry, isDir));
            }

            nodes.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (UnauthorizedAccessException) { }

        return nodes;
    }

    public void CreateFolder(string parentPath, string folderName)
    {
        string newPath = Path.Combine(parentPath, folderName);
        if (!Directory.Exists(newPath))
        {
            Directory.CreateDirectory(newPath);
        }
    }

    public void CreateFile(string parentPath, string fileName)
    {
        string newPath = Path.Combine(parentPath, fileName);
        if (!File.Exists(newPath))
        {
            File.Create(newPath).Dispose();
        }
    }

    public void RenameItem(string oldPath, string newName)
    {
        string newPath = Path.Combine(Path.GetDirectoryName(oldPath) ?? string.Empty, newName);
        if (Directory.Exists(oldPath))
            Directory.Move(oldPath, newPath);
        else if (File.Exists(oldPath))
            File.Move(oldPath, newPath);
    }

    public void DeleteItem(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    public void DuplicateItem(string path)
    {
        if (File.Exists(path))
        {
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            string newPath = Path.Combine(dir, $"{name}_copy{ext}");
            int count = 1;
            while (File.Exists(newPath))
            {
                newPath = Path.Combine(dir, $"{name}_copy{count}{ext}");
                count++;
            }
            File.Copy(path, newPath);
        }
        else if (Directory.Exists(path))
        {
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string dirName = Path.GetFileName(path);
            string newPath = Path.Combine(dir, $"{dirName}_copy");
            int count = 1;
            while (Directory.Exists(newPath))
            {
                newPath = Path.Combine(dir, $"{dirName}_copy{count}");
                count++;
            }
            CopyDirectory(path, newPath);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;
        Directory.CreateDirectory(destinationDir);
        foreach (FileInfo file in dir.GetFiles())
        {
            file.CopyTo(Path.Combine(destinationDir, file.Name));
        }
        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            CopyDirectory(subDir.FullName, Path.Combine(destinationDir, subDir.Name));
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
