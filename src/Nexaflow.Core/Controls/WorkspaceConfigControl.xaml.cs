using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
using Nexaflow.Features.Common;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Core.Controls;

public partial class WorkspaceConfigControl : UserControl, ICustomConfigApply
{
    private ObservableCollection<Workspace>? _editWorkspaces;
    private WorkspacesConfig?              _config;

    private static readonly Random _rng = new();

    // The shared categorical bank (Tokens.xaml / per-theme overrides), resolved to brushes + hex for
    // the per-workspace colour picker. Same source the ribbon picker and project pie draw from.
    public IReadOnlyList<SwatchOption> Swatches { get; } = WorkspaceSwatches.Build();

    /// <summary>Random colour from the swatch bank (falls back to the legacy palette if unresolved).</summary>
    private string RandomSwatchColor()
        => Swatches.Count > 0 ? Swatches[_rng.Next(Swatches.Count)].Hex : WorkspaceStyle.Random().Color;

    // Maps each editable copy back to the original workspace so Apply can write the edited
    // Name/Color/Icon onto the original without disturbing live workspaces.
    private readonly Dictionary<Workspace, Workspace> _editToOriginal = [];

    // Maps an editable copy that represents an unsaved CLONE to the source workspace it should be
    // cloned from at Apply time (copying its AiConfig + provider configs).
    private readonly Dictionary<Workspace, Workspace> _editCloneSource = [];

    public WorkspaceConfigControl()
    {
        InitializeComponent();

        // List-level drag-over drives the ghost-frame position (tunnels, so it fires wherever the
        // cursor is within the list, not only over an item).
        WorkspaceItems.AllowDrop = true;
        WorkspaceItems.PreviewDragOver += OnListPreviewDragOver;

        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is WorkspacesConfig cfg)
            {
                _config = cfg;
                RebuildEditWorkspaces();
            }
        };
    }

    /// <summary>(Re)builds the editable row copies from <see cref="_config"/>'s current contexts.</summary>
    private void RebuildEditWorkspaces()
    {
        if (_config is null) return;
        _editToOriginal.Clear();
        _editCloneSource.Clear();
        _editWorkspaces = [];
        foreach (var p in _config.Contexts)
        {
            var edit = new Workspace
            {
                Name    = p.Name,
                Color   = p.Color,
                Icon    = p.Icon,
                IsInUse = WorkspaceManager.Instance.IsWorkspaceInUse(p),   // hides Delete on the live workspace
            };
            _editToOriginal[edit] = p;
            _editWorkspaces.Add(edit);
        }
        WorkspaceItems.ItemsSource = _editWorkspaces;
    }

    public void Apply()
    {
        if (_config is null || _editWorkspaces is null) return;

        var mgr       = WorkspaceManager.Instance;
        var survivors = new HashSet<Workspace>();
        var ordered   = new List<Workspace>();

        foreach (var edit in _editWorkspaces)
        {
            Workspace original;
            if (_editToOriginal.TryGetValue(edit, out var existing))
            {
                existing.Name  = edit.Name;
                existing.Color = edit.Color;
                existing.Icon  = edit.Icon;
                original = existing;
            }
            else if (_editCloneSource.TryGetValue(edit, out var source))
            {
                original = mgr.CloneWorkspace(source, edit.Name);
                original.Color = edit.Color;
                original.Icon  = edit.Icon;
                _editToOriginal[edit] = original;   // now a saved row (lets Configure resolve it)
                _editCloneSource.Remove(edit);
            }
            else
            {
                original = mgr.AddWorkspace(edit.Name);
                original.Color = edit.Color;
                original.Icon  = edit.Icon;
                _editToOriginal[edit] = original;
            }
            survivors.Add(original);
            ordered.Add(original);
        }

        // Remove workspaces the user deleted — but never one a live workspace is using, and never the last.
        foreach (var workspace in mgr.Workspaces.ToList())
            if (!survivors.Contains(workspace) && !mgr.IsWorkspaceInUse(workspace))
                mgr.RemoveWorkspace(workspace);

        // Sync the live Workspaces order to the edited order (drag-reorder), so the dropdown and the
        // persisted list match. SaveWorkspaces rebuilds Contexts from Workspaces, so order must live here.
        for (int i = 0; i < ordered.Count; i++)
        {
            int cur = mgr.Workspaces.IndexOf(ordered[i]);
            if (cur >= 0 && cur != i) mgr.Workspaces.Move(cur, i);
        }

        _config.Contexts = ordered.Count > 0 ? ordered : [.. mgr.Workspaces];
    }

    // Add Workspace: commit any pending edits, create the new workspace, then run the workspace-setup
    // wizard (provider → key → model → …) for it, and refresh the list (new workspace at the top).
    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;

        Apply();   // materialise existing edit rows first so nothing is lost

        var (icon, _) = WorkspaceStyle.Random();
        // Unique name => a fresh, empty config folder (avoids a re-used "New Workspace" dir picking up
        // a previous workspace's saved provider/model).
        var workspace = WorkspaceManager.Instance.AddWorkspace(
            WorkspaceManager.Instance.UniqueWorkspaceName("New Workspace"));
        workspace.Icon  = icon;
        workspace.Color = RandomSwatchColor();
        WorkspaceManager.Instance.Workspaces.Move(WorkspaceManager.Instance.Workspaces.Count - 1, 0);
        WorkspaceManager.Instance.SaveWorkspaces();

        var wizard = SetupWizardViewModel.BuildWorkspaceSetup(workspace);
        new SetupWizardWindow(wizard) { Owner = Window.GetWindow(this) }.ShowDialog();

        RebuildEditWorkspaces();
    }

    // Click a swatch → set the colour of the workspace that owns this row. Workspace is observable, so the
    // hex box and colour preview update live. The owning workspace is the nearest DataContext up the tree
    // (the swatch button's own DataContext is the SwatchOption).
    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex } fe) return;
        for (DependencyObject? d = fe; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: Workspace p })
            {
                p.Color = hex;
                return;
            }
    }

    // Configure the workspace for this row. Commits ALL pending workspace edits first (so a brand-new or
    // cloned row is materialised on disk and other renames/reorders/deletes aren't lost), then closes
    // Options and opens the per-workspace Configure panel for the resolved workspace.
    private void OnConfigureClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Workspace edit } || _config is null) return;

        Apply();                                    // create/update workspaces + sync order
        WorkspaceManager.Instance.SaveWorkspaces();   // persist now (we bypass the Options Save button)

        if (!_editToOriginal.TryGetValue(edit, out var original)) return;

        if (Window.GetWindow(this)?.DataContext is ShellViewModel shell)
            shell.OpenConfigureFromOptions(original);   // returns here if nothing is changed
    }

    private void OnCloneClick(object sender, RoutedEventArgs e)
    {
        if (_editWorkspaces is null) return;
        if (sender is not Button { Tag: Workspace edit }) return;

        var source = _editToOriginal.TryGetValue(edit, out var original) ? original
                   : _editCloneSource.TryGetValue(edit, out var src)     ? src
                   : null;
        if (source is null) return;   // a brand-new unsaved workspace has no persisted settings to copy

        var (icon, _) = WorkspaceStyle.Random();
        var clone = new Workspace { Name = edit.Name + " copy", Color = RandomSwatchColor(), Icon = icon };
        _editCloneSource[clone] = source;

        _editWorkspaces.Insert(_editWorkspaces.IndexOf(edit) + 1, clone);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_editWorkspaces is null || _editWorkspaces.Count <= 1) return;
        if (sender is not Button { Tag: Workspace edit }) return;

        var shell = Window.GetWindow(this)?.DataContext as ShellViewModel;

        // Cannot delete a workspace a live workspace is using (incl. the active one).
        if (_editToOriginal.TryGetValue(edit, out var original)
            && WorkspaceManager.Instance.IsWorkspaceInUse(original))
        {
            shell?.ShowErrorToast("This workspace is open and can't be removed.");
            return;
        }

        // Confirm via the in-shell overlay (above the Options popup), never an OS MessageBox.
        if (shell is not null)
            shell.ShowConfirmation("Remove workspace", $"Remove workspace “{edit.Name}”?",
                () => _editWorkspaces.Remove(edit), null);
        else
            _editWorkspaces.Remove(edit);
    }

    // ── Drag-to-reorder ───────────────────────────────────────────────────────

    private Workspace?      _dragItem;
    private DragAdorner?  _dragAdorner;
    private AdornerLayer? _adornerLayer;
    private Point         _dragGrab;   // cursor offset within the dragged row, so the ghost tracks it

    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Workspace p }) _dragItem = p;
    }

    private void OnDragHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null
            || sender is not FrameworkElement fe) return;

        // Ghost frame of the dragged row, following the cursor (positioned in OnListPreviewDragOver).
        if (WorkspaceItems.ItemContainerGenerator.ContainerFromItem(_dragItem) is FrameworkElement container)
        {
            _dragGrab = e.GetPosition(container);   // where within the row the user grabbed
            _adornerLayer = AdornerLayer.GetAdornerLayer(WorkspaceItems);
            if (_adornerLayer is not null)
            {
                _dragAdorner = new DragAdorner(WorkspaceItems, container,
                    new Size(container.ActualWidth, container.ActualHeight));
                _adornerLayer.Add(_dragAdorner);
            }
        }

        try
        {
            DragDrop.DoDragDrop(fe, _dragItem, DragDropEffects.Move);
        }
        finally
        {
            if (_dragAdorner is not null) _adornerLayer?.Remove(_dragAdorner);
            _dragAdorner  = null;
            _adornerLayer = null;
            _dragItem     = null;
        }
    }

    private void OnListPreviewDragOver(object sender, DragEventArgs e)
    {
        if (_dragAdorner is not null)
        {
            var p = e.GetPosition(WorkspaceItems);
            // Anchor the ghost so the grab point stays under the cursor.
            _dragAdorner.SetOffset(new Point(p.X - _dragGrab.X, p.Y - _dragGrab.Y));
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnItemDrop(object sender, DragEventArgs e)
    {
        if (_editWorkspaces is null) return;
        if (e.Data.GetData(typeof(Workspace)) is not Workspace dragged) return;
        if (sender is not FrameworkElement { DataContext: Workspace target }) return;
        if (ReferenceEquals(dragged, target)) return;

        int from = _editWorkspaces.IndexOf(dragged);
        int to   = _editWorkspaces.IndexOf(target);
        if (from < 0 || to < 0) return;
        _editWorkspaces.Move(from, to);
    }
}
