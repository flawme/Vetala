using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vetala.Models;
using Vetala.Services;

namespace Vetala.ViewModels;

public partial class SourceControlViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _branch = string.Empty;

    [ObservableProperty]
    private bool _isGitRepo;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<GitStatusEntry> ChangedFiles { get; } = new();

    public ObservableCollection<GitStatusEntry> UntrackedFiles { get; } = new();

    public bool HasChanges => ChangedFiles.Count > 0;

    public bool HasUntracked => UntrackedFiles.Count > 0;

    /// <summary>Provided by the main view model: opens a file in the editor.</summary>
    public Action<string>? OpenFileRequested;

    /// <summary>Notifies the main view model when the branch changes so the status bar can update.</summary>
    public Action<string?>? BranchUpdated;

    private string _projectPath = string.Empty;

    [RelayCommand]
    private Task Refresh() => RefreshAsync(_projectPath);

    public async Task RefreshAsync(string projectPath)
    {
        _projectPath = projectPath ?? string.Empty;
        if (string.IsNullOrEmpty(_projectPath)) return;

        IsLoading = true;
        try
        {
            var path = _projectPath;
            var branch = await Task.Run(() => GitService.GetBranch(path));
            var entries = await Task.Run(() => GitService.GetChangedFiles(path));

            Branch = branch ?? string.Empty;
            IsGitRepo = branch != null;
            BranchUpdated?.Invoke(branch);

            ChangedFiles.Clear();
            UntrackedFiles.Clear();
            foreach (var entry in entries)
            {
                if (entry.StatusCode == 'U')
                    UntrackedFiles.Add(entry);
                else
                    ChangedFiles.Add(entry);
            }

            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasUntracked));

            if (!IsGitRepo)
                StatusText = "Not a git repository";
            else
                StatusText = ChangedFiles.Count + UntrackedFiles.Count == 0
                    ? "No changes"
                    : $"{ChangedFiles.Count + UntrackedFiles.Count} changed file{(ChangedFiles.Count + UntrackedFiles.Count == 1 ? "" : "s")}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OpenFile(GitStatusEntry entry)
    {
        OpenFileRequested?.Invoke(entry.AbsolutePath);
    }
}
