using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The workspace-identity editor — the first page of the Configure (Manage-AI) panel. Edits a
/// <see cref="Workspace"/>'s Name / Symbol / Colour against an in-memory copy and only writes back to the
/// live workspace on <see cref="Apply"/> (which moves the on-disk data folder when the name changes), so
/// closing the panel without applying discards the edits — matching the panel's per-section Apply model.
/// </summary>
public partial class WorkspaceIdentityControl : UserControl, IConfigChangeTracker, IConfigValidation, ICustomConfigApply
{
    /// <summary>Shared categorical bank for the colour picker (same source as the Options list).</summary>
    public IReadOnlyList<SwatchOption> Swatches { get; } = WorkspaceSwatches.Build();

    // The live workspace being configured (set via DataContext by the host) and the bound editing copy.
    private Workspace? _target;
    private readonly Workspace _edit = new();

    public WorkspaceIdentityControl()
    {
        InitializeComponent();
        EditPanel.DataContext = _edit;            // fields/preview bind to the editing copy, not the live workspace
        _edit.PropertyChanged += OnEditChanged;
        DataContextChanged    += OnTargetChanged;
    }

    private void OnTargetChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Workspace p) return;
        _target     = p;
        _edit.Name  = p.Name;
        _edit.Color = p.Color;
        _edit.Icon  = p.Icon;

        // The last remaining workspace can't be deleted — disable the button rather than fail on click.
        bool canDelete       = WorkspaceManager.Instance.Workspaces.Count > 1;
        DeleteButton.IsEnabled = canDelete;
        DeleteButton.ToolTip   = canDelete ? null : "The last workspace can't be deleted.";

        Revalidate();
        RaiseChanged();
    }

    private void OnEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        Revalidate();
        RaiseChanged();
    }

    // ── IConfigChangeTracker: enables the panel's Apply button while the copy differs from the workspace ──
    public bool HasChanges =>
        _target is not null &&
        (_edit.Name != _target.Name || _edit.Color != _target.Color || _edit.Icon != _target.Icon);

    public event EventHandler? HasChangesChanged;
    private void RaiseChanged() => HasChangesChanged?.Invoke(this, EventArgs.Empty);

    // ── IConfigValidation: a name must be non-empty and not collide with another workspace's folder ──
    public bool IsValid { get; private set; } = true;
    public event EventHandler? IsValidChanged;

    private void Revalidate()
    {
        bool valid = !string.IsNullOrWhiteSpace(_edit.Name) && !NameCollides(_edit.Name);
        if (valid == IsValid) return;
        IsValid = valid;
        IsValidChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool NameCollides(string name)
        => WorkspaceManager.Instance.Workspaces.Any(
               p => !ReferenceEquals(p, _target)
                 && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    // ── ICustomConfigApply: commit the edits to the live workspace + disk ──
    public void Apply()
    {
        if (_target is null || !HasChanges || !IsValid) return;

        _target.Color = _edit.Color;
        _target.Icon  = _edit.Icon;
        // Renames the workspace and moves its data folder so the cloned/edited config follows; persists
        // the workspace list (incl. the colour/icon just set, even when the name is unchanged).
        WorkspaceManager.Instance.RenameWorkspace(_target, _edit.Name.Trim());

        _edit.Name = _target.Name;   // resnapshot to the committed (trimmed) name → HasChanges back to false
        RaiseChanged();
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex }) _edit.Color = hex;
    }

    // ── Export conversations ──
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_target is null) return;
        var owner = Window.GetWindow(this);
        var shell = owner?.DataContext as ShellViewModel;

        var dest = FolderBrowserWindow.Show(owner: owner);
        if (string.IsNullOrEmpty(dest)) return;   // cancelled

        try
        {
            int n = ConversationStore.ExportAll(_target.ConversationsDir, dest);
            shell?.ShowInfoToast("Export conversations",
                n == 0 ? "This workspace has no conversations to export."
                       : $"Exported {n} conversation{(n == 1 ? "" : "s")} to {dest}.");
        }
        catch (Exception ex)
        {
            shell?.ShowErrorToast($"Could not export conversations: {ex.Message}");
        }
    }

    // ── Delete workspace ── (confirmation + deletion are orchestrated by the shell)
    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_target is null) return;
        if (Window.GetWindow(this)?.DataContext is ShellViewModel shell)
            shell.RequestDeleteWorkspace(_target);
    }
}
