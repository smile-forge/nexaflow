using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Visuals.Common.Theming;
using System.Windows.Media;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>
/// Editable backlog item. Its status is a configured status <b>key</b> resolved live against the
/// workspace config, so a deleted status shows as invalid (greyed, non-progressable) rather than being
/// silently remapped. The forward "Progress" button steps one status; the themed status dropdown sets
/// any status (binding <see cref="StatusKey"/> two-way, which persists via the parent).
/// </summary>
public partial class BacklogItemViewModel : ObservableObject
{
    private readonly ProjectDetailViewModel _parent;
    private bool _suppressStatusSave;

    public Guid Id { get; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _description = string.Empty;   // markdown
    [ObservableProperty] private string _statusKey = string.Empty;

    /// <summary>The workspace config — the status dropdown binds <c>Config.BacklogStatuses</c>.</summary>
    public ProjectsConfig Config => _parent.Config;

    public string StatusLabel     => Config.Resolve(StatusKey).Label;
    public bool   IsInvalidStatus => !Config.Resolve(StatusKey).IsValid;
    public bool   IsCancelled     => Config.Resolve(StatusKey).IsTerminalCancelled;
    public bool   CanProgress     => _parent.CanProgress(StatusKey);
    public Brush  StatusBrush     => SwatchPalette.Resolve(Config.Resolve(StatusKey).SwatchKey);

    public BacklogItemViewModel(BacklogItem item, ProjectDetailViewModel parent)
    {
        _parent      = parent;
        Id           = item.Id;
        _title       = item.Title;
        _description = item.Description;
        _statusKey   = item.StatusKey;
    }

    partial void OnTitleChanged(string value) { /* saved explicitly via the Save button */ }
    partial void OnDescriptionChanged(string value) { /* saved explicitly via the Save button */ }

    partial void OnStatusKeyChanged(string value)
    {
        RefreshStatusView();
        if (_suppressStatusSave) return;   // programmatic (Progress / reload) — don't re-persist
        _parent.SetItemStatus(this, value);
    }

    private void RefreshStatusView()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsInvalidStatus));
        OnPropertyChanged(nameof(CanProgress));
        ProgressCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Sets the status key without re-persisting — used after Progress or a reload.</summary>
    internal void SetStatusKeyQuiet(string key)
    {
        _suppressStatusSave = true;
        StatusKey = key;
        _suppressStatusSave = false;
    }

    [RelayCommand(CanExecute = nameof(CanProgress))]
    private void Progress() => _parent.ProgressItem(this);
}
