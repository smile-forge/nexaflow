using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Visuals.Common.Controls;
using System.Collections.ObjectModel;
using System.Windows;
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

    /// <summary>Description for the detail panel, with a placeholder when none is set.</summary>
    public string DescriptionDisplay
        => string.IsNullOrWhiteSpace(Description) ? "This project does not have a description" : Description;
}

// ── Main view-model ───────────────────────────────────────────────────────────

public partial class ProjectsViewModel : ObservableObject, IPageViewModel
{
    private readonly ProjectOperations _svc;

    public ObservableCollection<ProjectSummaryItem> Projects { get; } = [];

    [ObservableProperty] private ProjectSummaryItem? _selectedProject;
    [ObservableProperty] private int _projectCount;

    /// <summary>False when the Projects feature is disabled for this workspace — the view shows a
    /// "enable in settings" placeholder instead of the list/detail split.</summary>
    [ObservableProperty] private bool _isEnabled;

    // Raised when the user clicks "Open Project" — shell wires this to open a tab
    public event Action<string>? OpenProjectRequested;

    // Raised when the user clicks "Open Files" — shell opens a FileSystem tab at that path
    public event Action<string>? OpenFilesRequested;

    // ── Status-bucket colours (shared with detail view) ───────────────────
    // Read from the active theme at call-time; theme restarts the window so these are stable
    // within any one session. Fallback colours match the Dark-theme palette exactly.
    // Resolve from the active theme; throw (not silently fall back to a literal) if the key is missing,
    // so a mis-typed or undefined token surfaces immediately instead of painting a plausible colour.
    private static Brush Res(string key)
        => Application.Current?.Resources[key] as Brush
           ?? throw new InvalidOperationException($"Theme brush '{key}' not found.");
    // Distinct categorical swatches — the four buckets must read apart in a pie/legend, so they pull
    // from the swatch bank (slate / blue / green / red), not the theme's close-together chrome tones.
    internal static Brush BrushNotStarted => Res("Swatch.Slate");
    internal static Brush BrushInProgress => Res("Swatch.Blue");
    internal static Brush BrushDone       => Res("Swatch.Green");
    internal static Brush BrushCancelled  => Res("Swatch.Red");

    public ProjectsViewModel(ProjectOperations ops, bool isEnabled)
    {
        _svc = ops;
        IsEnabled = isEnabled;
        if (isEnabled) Load();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (IsEnabled) Load();
    }

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
        SelectedProject = Projects.FirstOrDefault();
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
