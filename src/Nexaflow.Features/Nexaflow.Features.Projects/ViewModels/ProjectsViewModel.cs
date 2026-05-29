using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Visuals.Common.Controls;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Nexaflow.Features.Projects.ViewModels;

// ── Summary row shown in the project list ─────────────────────────────────────

public partial class ProjectSummaryItem : ObservableObject
{
    public string FolderName { get; init; } = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _lastModified = string.Empty;
    [ObservableProperty] private int    _activeCount;
    [ObservableProperty] private int    _cancelledCount;
    [ObservableProperty] private int    _doneCount;
    [ObservableProperty] private int    _totalCount;
    [ObservableProperty] private IReadOnlyList<PieSlice> _pieSlices = [];

    /// <summary>First two non-empty lines of the description, used in the list banner.</summary>
    public string DescriptionPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Description)) return string.Empty;
            var lines = Description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(2);
            return string.Join(" ", lines);
        }
    }
}

// ── Main view-model ───────────────────────────────────────────────────────────

public partial class ProjectsViewModel : ObservableObject, IPageViewModel
{
    private readonly ProjectOperations _svc;

    public ObservableCollection<ProjectSummaryItem> Projects { get; } = [];

    [ObservableProperty] private ProjectSummaryItem? _selectedProject;
    [ObservableProperty] private int _projectCount;

    // Raised when the user clicks "Open Project" — shell wires this to open a tab
    public event Action<string>? OpenProjectRequested;

    // Raised when the user clicks "Open Files" — shell opens a FileSystem tab at that path
    public event Action<string>? OpenFilesRequested;

    // ── Status-bucket colours (shared with detail view) ───────────────────
    internal static readonly SolidColorBrush BrushNotStarted  = new(Color.FromRgb(0x4A, 0x52, 0x70));
    internal static readonly SolidColorBrush BrushInProgress  = new(Color.FromRgb(0x4F, 0x8E, 0xF7));
    internal static readonly SolidColorBrush BrushDone        = new(Color.FromRgb(0x22, 0xD3, 0xA5));
    internal static readonly SolidColorBrush BrushCancelled   = new(Color.FromRgb(0x7C, 0x5C, 0xFC));

    static ProjectsViewModel()
    {
        BrushNotStarted.Freeze();
        BrushInProgress.Freeze();
        BrushDone.Freeze();
        BrushCancelled.Freeze();
    }

    public ProjectsViewModel(ProjectOperations ops)
    {
        _svc = ops;
        Load();
    }

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        Projects.Clear();
        SelectedProject = null;

        try
        {
            foreach (var (folder, name) in _svc.GetProjectListTyped())
            {
                var info = _svc.GetProjectInfo(folder);
                Projects.Add(BuildSummary(folder, name, info));
            }
        }
        catch { /* show empty list on error */ }

        ProjectCount = Projects.Count;
    }

    private static ProjectSummaryItem BuildSummary(string folder, string name, ProjectInfo info)
    {
        var notStarted = info.Backlog.Count(i => i.Status == BacklogStatus.NotStarted);
        var inProgress = info.Backlog.Count(i =>
            i.Status != BacklogStatus.NotStarted &&
            i.Status != BacklogStatus.AwaitingFinalisation &&
            i.Status != BacklogStatus.Cancelled);
        var done       = info.Backlog.Count(i => i.Status == BacklogStatus.AwaitingFinalisation);
        var cancelled  = info.Backlog.Count(i => i.Status == BacklogStatus.Cancelled);
        var active     = info.Backlog.Count(i => i.Status != BacklogStatus.Cancelled);

        var slices = new List<PieSlice>();
        if (notStarted > 0) slices.Add(new PieSlice(notStarted, BrushNotStarted));
        if (inProgress > 0) slices.Add(new PieSlice(inProgress, BrushInProgress));
        if (done       > 0) slices.Add(new PieSlice(done,       BrushDone));
        if (cancelled  > 0) slices.Add(new PieSlice(cancelled,  BrushCancelled));

        return new ProjectSummaryItem
        {
            FolderName    = folder,
            DisplayName   = string.IsNullOrWhiteSpace(info.Name) ? folder : info.Name,
            Description   = info.Description,
            LastModified  = info.LastUpdate.HasValue
                                ? info.LastUpdate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                                : "—",
            ActiveCount   = active,
            CancelledCount = cancelled,
            DoneCount     = done,
            TotalCount    = info.Backlog.Count,
            PieSlices     = slices
        };
    }

    [RelayCommand]
    private void OpenProject(ProjectSummaryItem? item)
    {
        if (item is null) return;
        OpenProjectRequested?.Invoke(item.FolderName);
    }

    [RelayCommand]
    private void OpenFiles(ProjectSummaryItem? item)
    {
        var folder = item?.FolderName ?? SelectedProject?.FolderName;
        if (folder is null) return;
        OpenFilesRequested?.Invoke(System.IO.Path.Combine(_svc.RootPath, folder));
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        if (ProjectCount == 0) return "Projects list: no projects.";
        var selected = SelectedProject is { } p ? $" Selected: '{p.DisplayName}'." : string.Empty;
        return $"Projects list: {ProjectCount} project(s).{selected}";
    }
}
