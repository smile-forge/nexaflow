using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Projects.Model;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;

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
        if (ProjectCount == 0)
            return IsEnabled
                ? $"Projects list ({SelectedBucket}): no projects."
                : "Projects list: no projects (the feature is disabled for this workspace).";

        var lines = Projects.Take(40).Select(p =>
        {
            var desc = string.IsNullOrWhiteSpace(p.DescriptionPreview) ? string.Empty : $" — {p.DescriptionPreview}";
            return $"• {p.DisplayName} ({p.CountsText}){desc}";
        });
        var more     = ProjectCount > 40 ? $"\n(+{ProjectCount - 40} more)" : string.Empty;
        var selected = SelectedProject is { } s ? $" Selected: '{s.DisplayName}'." : string.Empty;

        return $"Projects list ({SelectedBucket}): {ProjectCount} project(s).{selected}\n"
             + string.Join("\n", lines) + more + "\n"
             + "Use list_projects for every project, or read_project to read a project's description, "
             + "completion criteria and backlog items.";
    }

    /// <summary>Scope = the folder root the current bucket reads from, so two Projects tabs on different
    /// buckets stay distinguishable when pinned together.</summary>
    public string? GetSecurityContext() => RootFor(SelectedBucket);

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "list_projects",
            "List every project in the current bucket (active / shelf / archive) with its display name, "
          + "folder, backlog count and last-modified time.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                if (!IsEnabled)
                    return Task.FromResult(ToolResult.Ok("disabled", "The Projects feature is disabled for this workspace."));
                if (Projects.Count == 0)
                    return Task.FromResult(ToolResult.Ok("no projects", $"No projects in the {SelectedBucket} bucket."));

                var body = string.Join("\n", Projects.Select(p =>
                {
                    var desc = string.IsNullOrWhiteSpace(p.DescriptionPreview) ? string.Empty : $" — {p.DescriptionPreview}";
                    return $"- {p.DisplayName} [{p.FolderName}] ({p.CountsText}, updated {p.LastModified}){desc}";
                }));
                return Task.FromResult(ToolResult.Ok($"{Projects.Count} project(s)", body));
            }),

        new DelegateClientTool(
            "read_project",
            "Read one project in full: its description, completion criteria and every backlog item "
          + "(title, status, detail). Identify it by folder name or display name; defaults to the "
          + "selected project.",
            [new ClientToolParameter("project",
                "Folder name or display name of the project. Optional — defaults to the selected project.",
                Required: false)],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                if (!IsEnabled)
                    return Task.FromResult(ToolResult.Error("The Projects feature is disabled for this workspace."));

                var key  = ToolArgs.Str(args, "project", "name", "folder", "id");
                var item = key is null
                    ? SelectedProject
                    : Projects.FirstOrDefault(p =>
                          string.Equals(p.FolderName,  key, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(p.DisplayName, key, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                    return Task.FromResult(ToolResult.Error(
                        key is null ? "No project is selected."
                                    : $"No project named '{key}' in the {SelectedBucket} bucket."));
                try
                {
                    var readout = OpsFor(SelectedBucket).GetProjectReadout(item.FolderName);
                    return Task.FromResult(ToolResult.Ok($"read '{item.DisplayName}'", readout));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(ToolResult.Error($"Could not read '{item.DisplayName}': {ex.Message}"));
                }
            }),
    ];
}
