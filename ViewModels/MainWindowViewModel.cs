using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vetala.Models;
using Vetala.Services;

namespace Vetala.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcomeVisible))]
    private bool _isProjectOpen = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectsPage))]
    [NotifyPropertyChangedFor(nameof(IsLicensesPage))]
    [NotifyPropertyChangedFor(nameof(IsHowToRunPage))]
    [NotifyPropertyChangedFor(nameof(IsResourcesPage))]
    private string _currentPage = "Projects";

    [ObservableProperty]
    private bool _isPermissionDialogVisible = false;

    [ObservableProperty]
    private string _pendingProjectPath = string.Empty;

    [ObservableProperty]
    private string _pendingProjectName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentProjectDisplay))]
    private string _openProjectPath = string.Empty;

    public string CurrentProjectDisplay => Path.GetFileName(OpenProjectPath);

    public bool IsWelcomeVisible => !IsProjectOpen;
    public bool IsProjectsPage => CurrentPage == "Projects";
    public bool IsLicensesPage => CurrentPage == "Licenses";
    public bool IsHowToRunPage => CurrentPage == "HowToRun";
    public bool IsResourcesPage => CurrentPage == "Resources";

    public EditorViewModel Editor { get; } = new();

    public SidebarViewModel Sidebar { get; } = new();

    public SearchViewModel Search { get; } = new();

    public SourceControlViewModel SourceControl { get; } = new();

    // Status Bar
    [ObservableProperty]
    private string _statusLine = "Ln 1";

    [ObservableProperty]
    private string _statusColumn = "Col 1";

    [ObservableProperty]
    private string _statusEncoding = "UTF-8";

    [ObservableProperty]
    private string _statusLineEnding = "LF";

    [ObservableProperty]
    private string _statusLanguage = "Plain Text";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGitBranch))]
    private string _statusGitBranch = string.Empty;

    public bool HasGitBranch => !string.IsNullOrEmpty(StatusGitBranch);

    [ObservableProperty]
    private string _statusSpaces = "Spaces: 4";

    [ObservableProperty]
    private string _breadcrumbPath = "";

    // Sidebar panels: "", "Explorer", "Search", "SourceControl"
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsExplorerVisible))]
    [NotifyPropertyChangedFor(nameof(IsSearchVisible))]
    [NotifyPropertyChangedFor(nameof(IsSourceControlVisible))]
    private string _sidebarMode = "Explorer";

    public bool IsSidebarVisible => SidebarMode.Length > 0;
    public bool IsExplorerVisible => SidebarMode == "Explorer";
    public bool IsSearchVisible => SidebarMode == "Search";
    public bool IsSourceControlVisible => SidebarMode == "SourceControl";

    [ObservableProperty]
    private Avalonia.Controls.GridLength _explorerColumnWidth = new(250, Avalonia.Controls.GridUnitType.Pixel);

    private double _savedSidebarWidth = 250;

    [RelayCommand]
    private void ToggleExplorer() => ToggleSidebarPanel("Explorer");

    [RelayCommand]
    private void ToggleSearch() => ToggleSidebarPanel("Search");

    [RelayCommand]
    private void ToggleSourceControl() => ToggleSidebarPanel("SourceControl");

    private void ToggleSidebarPanel(string mode)
    {
        if (SidebarMode == mode)
        {
            // Clicking the active panel collapses the sidebar
            if (ExplorerColumnWidth.Value > 0)
                _savedSidebarWidth = ExplorerColumnWidth.Value;
            ExplorerColumnWidth = new Avalonia.Controls.GridLength(0, Avalonia.Controls.GridUnitType.Pixel);
            SidebarMode = string.Empty;
        }
        else
        {
            if (ExplorerColumnWidth.Value <= 0)
                ExplorerColumnWidth = new Avalonia.Controls.GridLength(_savedSidebarWidth, Avalonia.Controls.GridUnitType.Pixel);
            SidebarMode = mode;

            if (mode == "SourceControl" && !string.IsNullOrEmpty(OpenProjectPath))
                _ = SourceControl.RefreshAsync(OpenProjectPath);
        }
    }

    public MainWindowViewModel()
    {
        Sidebar.OnFileOpened = async (filePath) => await Editor.OpenFile(filePath);

        Search.GetProjectRoot = () => OpenProjectPath;
        Search.OpenResultRequested = async (filePath, line) =>
        {
            await Editor.OpenFile(filePath);
            Editor.RequestNavigateToLine(line);
        };

        SourceControl.OpenFileRequested = (filePath) => _ = Editor.OpenFile(filePath);
        SourceControl.BranchUpdated = (branch) => StatusGitBranch = branch ?? string.Empty;

        Editor.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.ActiveTab) && Editor.ActiveTab != null)
            {
                var tab = Editor.ActiveTab;
                var ext = Path.GetExtension(tab.FilePath).ToLowerInvariant();
                StatusLanguage = ext switch
                {
                    ".cs" => "C#",
                    ".ts" => "TypeScript",
                    ".tsx" => "TypeScript React",
                    ".js" => "JavaScript",
                    ".jsx" => "JavaScript React",
                    ".json" => "JSON",
                    ".xml" => "XML",
                    ".axaml" => "AXAML",
                    ".xaml" => "XAML",
                    ".html" => "HTML",
                    ".css" => "CSS",
                    ".md" => "Markdown",
                    ".py" => "Python",
                    ".rs" => "Rust",
                    ".go" => "Go",
                    ".yaml" or ".yml" => "YAML",
                    ".sh" => "Shell Script",
                    ".sql" => "SQL",
                    ".csproj" => "XML",
                    _ => "Plain Text"
                };

                if (!string.IsNullOrEmpty(OpenProjectPath) && tab.FilePath.StartsWith(OpenProjectPath))
                {
                    var relative = Path.GetRelativePath(OpenProjectPath, tab.FilePath);
                    BreadcrumbPath = relative.Replace(Path.DirectorySeparatorChar, '/');
                }
                else
                {
                    BreadcrumbPath = tab.Title;
                }
            }
            else if (e.PropertyName == nameof(EditorViewModel.CaretLine) || e.PropertyName == nameof(EditorViewModel.CaretColumn))
            {
                StatusLine = $"Ln {Editor.CaretLine}";
                StatusColumn = $"Col {Editor.CaretColumn}";
            }
        };
    }

    public ObservableCollection<RecentProject> RecentProjects { get; } = new();

    public bool HasRecentProjects => RecentProjects.Count > 0;

    [RelayCommand]
    private void NavigateTo(string page)
    {
        CurrentPage = page;
    }

    public void RequestPermission(string folderPath)
    {
        PendingProjectPath = folderPath;
        PendingProjectName = Path.GetFileName(folderPath) ?? folderPath;
        IsPermissionDialogVisible = true;
    }

    [RelayCommand]
    private void GrantPermission()
    {
        IsPermissionDialogVisible = false;
        OpenProjectPath = PendingProjectPath;
        IsProjectOpen = true;
        Sidebar.LoadProject(OpenProjectPath);
        StatusGitBranch = GitService.GetBranch(OpenProjectPath) ?? string.Empty;
        _ = SourceControl.RefreshAsync(OpenProjectPath);
        PendingProjectPath = string.Empty;
        PendingProjectName = string.Empty;
    }

    [RelayCommand]
    private void DenyPermission()
    {
        IsPermissionDialogVisible = false;
        PendingProjectPath = string.Empty;
        PendingProjectName = string.Empty;
    }
}
