using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using System.Collections.ObjectModel;
using System.IO;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>Which project folder location the list is showing.</summary>
public enum ProjectBucket { Projects, Shelf, Archive }

// ── Summary row shown in the project list ─────────────────────────────────────

public partial class ProjectSummaryItem : ObservableObject
{
    public string FolderName { get; init; } = string.Empty;

    /// <summary>Absolute folder path (used for moves, Open Files, and opening the detail view).</summary>
    public string FolderPath { get; init; } = string.Empty;

    [ObservableProperty] private string _displayName  = string.Empty;
    [ObservableProperty] private string _description  = string.Empty;   // raw markdown
    [ObservableProperty] private string _detailMarkdown = string.Empty; // status pie + description
    [ObservableProperty] private string _countsText   = string.Empty;
    [ObservableProperty] private string _lastModified = string.Empty;

    /// <summary>First two non-empty lines of the description, shown on the list row.</summary>
    public string DescriptionPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Description)) return string.Empty;
            var lines = Description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2);
            return string.Join(" ", lines);
        }
    }
}

// ── Main view-model ───────────────────────────────────────────────────────────

public partial class ProjectsViewModel : ObservableObject, IPageViewModel
{
    private readonly ProjectsConfig  _config;
    private readonly IShellServices? _shell;

    public ObservableCollection<ProjectSummaryItem> Projects { get; } = [];

    [ObservableProperty] private ProjectSummaryItem? _selectedProject;
    [ObservableProperty] private int _projectCount;
    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty] private ProjectBucket _selectedBucket = ProjectBucket.Projects;

    // Moving overlay (shown in place of the detail pane while a background move runs).
    [ObservableProperty] private bool   _isMoving;
    [ObservableProperty] private string _movingMessage = string.Empty;

    /// <summary>In the Projects bucket a project can be archived or shelved…</summary>
    public bool CanArchiveOrShelf => SelectedBucket == ProjectBucket.Projects;
    /// <summary>…in Shelf / Archive it can be reactivated.</summary>
    public bool CanReactivate => SelectedBucket != ProjectBucket.Projects;

    // Raised when "Open Project" / "Open Files" is clicked (absolute path). Wired by the registration.
    public event Action<string>? OpenProjectRequested;
    public event Action<string>? OpenFilesRequested;

    public ProjectsViewModel(ProjectsConfig config, IShellServices? shell = null)
    {
        _config = config;
        _shell  = shell;
        IsEnabled = config.EnableProjects;
        if (IsEnabled) Load();
    }

    partial void OnSelectedBucketChanged(ProjectBucket value)
    {
        OnPropertyChanged(nameof(CanArchiveOrShelf));
        OnPropertyChanged(nameof(CanReactivate));
        if (IsEnabled) Load();
    }

    private string RootFor(ProjectBucket bucket) => bucket switch
    {
        ProjectBucket.Shelf   => _config.ShelfDirectory,
        ProjectBucket.Archive => _config.ArchiveDirectory,
        _                     => _config.ProjectDirectory,
    };

    private ProjectOperations OpsFor(ProjectBucket bucket) => new(_config, RootFor(bucket));

    [RelayCommand]
    private void Refresh()
    {
        if (IsEnabled) Load();
    }

    private void Load()
    {
        Projects.Clear();
        SelectedProject = null;

        var ops  = OpsFor(SelectedBucket);
        var root = RootFor(SelectedBucket);
        try
        {
            foreach (var (folder, name) in ops.GetProjectListTyped())
            {
                var info = ops.GetProjectInfo(folder);
                Projects.Add(BuildSummary(ops, root, folder, name, info));
            }
        }
        catch { /* show empty list on error (e.g. root folder missing) */ }

        ProjectCount    = Projects.Count;
        SelectedProject = Projects.FirstOrDefault();
    }

    private static ProjectSummaryItem BuildSummary(ProjectOperations ops, string root, string folder, string name, ProjectInfo info)
    {
        var statusMd = ops.GetBacklogStatusMarkdown(info);
        var detail = string.Join("\n\n",
            new[] { statusMd, info.Description }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new ProjectSummaryItem
        {
            FolderName     = folder,
            FolderPath     = Path.Combine(root, folder),
            DisplayName    = string.IsNullOrWhiteSpace(info.Name) ? folder : info.Name,
            Description    = info.Description,
            DetailMarkdown = detail,
            CountsText     = info.Backlog.Count == 0 ? "No backlog items" : $"{info.Backlog.Count} backlog item(s)",
            LastModified   = info.LastUpdate.HasValue
                                ? info.LastUpdate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                                : "—",
        };
    }

    // ── Open ──

    [RelayCommand]
    private void OpenProject(ProjectSummaryItem? item)
    {
        item ??= SelectedProject;
        if (item is null) return;
        OpenProjectRequested?.Invoke(item.FolderPath);
    }

    [RelayCommand]
    private void OpenFiles(ProjectSummaryItem? item)
    {
        var path = (item ?? SelectedProject)?.FolderPath;
        if (path is null) return;
        OpenFilesRequested?.Invoke(path);
    }

    // ── Move (archive / shelf / reactivate) ──

    [RelayCommand]
    private void Archive(ProjectSummaryItem? item)   => Move(item, _config.ArchiveDirectory);

    [RelayCommand]
    private void Shelf(ProjectSummaryItem? item)     => Move(item, _config.ShelfDirectory);

    [RelayCommand]
    private void Reactivate(ProjectSummaryItem? item) => Move(item, _config.ProjectDirectory);

    private void Move(ProjectSummaryItem? item, string targetRoot)
    {
        item ??= SelectedProject;
        if (item is null || _shell is null || IsMoving) return;
        if (string.IsNullOrWhiteSpace(targetRoot)) { _shell.ShowError("The target folder is not configured."); return; }

        var dest = Path.Combine(targetRoot, item.FolderName);
        MovingMessage = "Moving project… please wait";
        IsMoving = true;
        _shell.MoveFolderInBackground(item.FolderPath, dest, MovingMessage, ok =>
        {
            IsMoving = false;
            if (!ok) _shell.ShowError($"Could not move '{item.DisplayName}'.");
            Load();
        });
    }

    // ── IPageViewModel ──

    public string GetContext()
    {
        if (ProjectCount == 0) return "Projects list: no projects.";
        var selected = SelectedProject is { } p ? $" Selected: '{p.DisplayName}'." : string.Empty;
        return $"Projects list ({SelectedBucket}): {ProjectCount} project(s).{selected}";
    }
}
