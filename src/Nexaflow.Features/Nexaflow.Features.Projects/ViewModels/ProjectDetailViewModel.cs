using Nexaflow.Features.Common.Controls;
using Nexaflow.Features.Projects.Services;
using Nexaflow.Features.Projects.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Nexaflow.Features.Projects.ViewModels;



// ── Main detail view-model ────────────────────────────────────────────────────

public partial class ProjectDetailViewModel : ObservableObject
{
    private readonly ProjectOperations _svc = ProjectService.Ops;
    public  string FolderName { get; }

    // ── Project header ────────────────────────────────────────────────────
    [ObservableProperty] private string _projectName    = string.Empty;
    [ObservableProperty] private string _description    = string.Empty;
    [ObservableProperty] private string _scope          = string.Empty;
    [ObservableProperty] private string _objectivesText = string.Empty;  // newline-separated
    [ObservableProperty] private string _lastModified   = string.Empty;
    [ObservableProperty] private string? _solutionFile;

    // ── Backlog ────────────────────────────────────────────────────────────
    public ObservableCollection<BacklogItemViewModel> Backlog { get; } = [];

    [ObservableProperty] private BacklogItemViewModel? _selectedItem;

    // ── New todo entry ────────────────────────────────────────────────────
    [ObservableProperty] private string _newTodoTitle = string.Empty;

    // ── Pie chart ─────────────────────────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<PieSlice> _pieSlices = [];

    // ── Confirmation overlay (same pattern as FileSystemViewModel) ────────
    [ObservableProperty] private bool   _confirmationVisible;
    [ObservableProperty] private string _confirmationTitle  = "Are you sure?";
    [ObservableProperty] private string _confirmationPrompt = string.Empty;
    private Action? _pendingConfirmAction;

    [RelayCommand]
    private void ConfirmAction()
    {
        ConfirmationVisible = false;
        _pendingConfirmAction?.Invoke();
        _pendingConfirmAction = null;
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        ConfirmationVisible = false;
        _pendingConfirmAction = null;
    }

    private void ShowConfirmation(string title, string prompt, Action onConfirm)
    {
        ConfirmationTitle   = title;
        ConfirmationPrompt  = prompt;
        _pendingConfirmAction = onConfirm;
        ConfirmationVisible = true;
    }

    // ── Raised by shell to open the project files tab ─────────────────────
    public event Action<string>? OpenFilesRequested;

    // ── Dirty-save guards ─────────────────────────────────────────────────
    private bool _loading;

    public ProjectDetailViewModel(string folderName)
    {
        FolderName = folderName;
        Load();
    }

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        _loading = true;
        try
        {
            var info = _svc.GetProjectInfo(FolderName);
            ProjectName    = string.IsNullOrWhiteSpace(info.Name) ? FolderName : info.Name;
            Description    = info.Description;
            Scope          = info.Scope;
            ObjectivesText = string.Join(Environment.NewLine, info.Objectives);
            SolutionFile   = info.SolutionFile;
            LastModified   = info.LastUpdate.HasValue
                ? info.LastUpdate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "—";

            Backlog.Clear();
            foreach (var item in info.Backlog)
                Backlog.Add(new BacklogItemViewModel(item, this));

            RefreshPie();
        }
        finally { _loading = false; }
    }

    // ── Header saves ──────────────────────────────────────────────────────

    partial void OnProjectNameChanged(string value)  => SaveHeader();
    partial void OnDescriptionChanged(string value)  => SaveHeader();
    partial void OnScopeChanged(string value)        => SaveScope();
    partial void OnObjectivesTextChanged(string value) => SaveObjectives();

    private void SaveHeader()
    {
        if (_loading) return;
        _svc.ModifyProjectHeader(FolderName, ProjectName, Description);
        RefreshLastModified();
    }

    private void SaveScope()
    {
        if (_loading) return;
        _svc.ModifyScope(FolderName, Scope);
        RefreshLastModified();
    }

    private void SaveObjectives()
    {
        if (_loading) return;
        var lines = ObjectivesText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _svc.ClearObjectives(FolderName);
        if (lines.Count > 0) _svc.AddObjectives(FolderName, lines);
        RefreshLastModified();
    }

    private void RefreshLastModified()
    {
        // Re-read info for the updated timestamp
        var info = _svc.GetProjectInfo(FolderName);
        LastModified = info.LastUpdate.HasValue
            ? info.LastUpdate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "—";
    }

    // ── Backlog operations (called by BacklogItemViewModel) ───────────────

    [RelayCommand]
    private void SaveItem()
    {
        if (SelectedItem is not null) SaveBacklogItem(SelectedItem);
    }

    internal void SaveBacklogItem(BacklogItemViewModel vm)
    {
        if (_loading) return;
        _svc.ModifyToDo(FolderName, vm.Id, vm.Title, vm.Notes);
        if (vm.ImpPlan is not null)  _svc.ModifyToDoImpPlan(FolderName, vm.Id, vm.ImpPlan);
        if (vm.TestPlan is not null) _svc.ModifyToDoTestPlan(FolderName, vm.Id, vm.TestPlan);
        RefreshLastModified();
    }

    internal void ProgressItem(BacklogItemViewModel vm)
    {
        _svc.ProgressToDo(FolderName, vm.Id);
        var info = _svc.GetProjectInfo(FolderName);
        var updated = info.Backlog.FirstOrDefault(i => i.Id == vm.Id);
        if (updated is not null) vm.Status = updated.Status;
        RefreshPie();
        RefreshLastModified();
    }

    internal void CancelItem(BacklogItemViewModel vm)
    {
        _svc.CancelToDo(FolderName, vm.Id);
        vm.Status = BacklogStatus.Cancelled;
        RefreshPie();
        RefreshLastModified();
    }

    [RelayCommand]
    private void AddTodo()
    {
        var title = NewTodoTitle.Trim();
        if (string.IsNullOrEmpty(title)) return;
        var id = _svc.AddToDo(FolderName, title);
        Backlog.Add(new BacklogItemViewModel(
            new BacklogItem { Id = id, Title = title }, this));
        NewTodoTitle = string.Empty;
        RefreshPie();
        RefreshLastModified();
    }

    [RelayCommand]
    private void DeleteTodo(BacklogItemViewModel? item)
    {
        if (item is null) return;
        ShowConfirmation(
            "Delete backlog item",
            $"Delete \"{item.Title}\"? This cannot be undone.",
            () =>
            {
                if (!item.IsCancelled)
                    _svc.CancelToDo(FolderName, item.Id);
                Backlog.Remove(item);
                if (SelectedItem == item) SelectedItem = null;
                RefreshPie();
                RefreshLastModified();
            });
    }

    [RelayCommand]
    private void OpenFiles()
        => OpenFilesRequested?.Invoke(ProjectService.GetProjectPath(FolderName));

    // ── Pie chart ─────────────────────────────────────────────────────────

    private void RefreshPie()
    {
        var notStarted = Backlog.Count(i => i.Status == BacklogStatus.NotStarted);
        var inProgress = Backlog.Count(i =>
            i.Status != BacklogStatus.NotStarted &&
            i.Status != BacklogStatus.AwaitingFinalisation &&
            i.Status != BacklogStatus.Cancelled);
        var done       = Backlog.Count(i => i.Status == BacklogStatus.AwaitingFinalisation);
        var cancelled  = Backlog.Count(i => i.Status == BacklogStatus.Cancelled);

        var slices = new List<PieSlice>();
        if (notStarted > 0) slices.Add(new PieSlice(notStarted, ProjectsViewModel.BrushNotStarted));
        if (inProgress > 0) slices.Add(new PieSlice(inProgress, ProjectsViewModel.BrushInProgress));
        if (done       > 0) slices.Add(new PieSlice(done,       ProjectsViewModel.BrushDone));
        if (cancelled  > 0) slices.Add(new PieSlice(cancelled,  ProjectsViewModel.BrushCancelled));

        PieSlices = slices;
    }
}
