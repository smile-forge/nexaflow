using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The "Default tabs" page of the Configure panel: lists the tabs a fresh window opens for this workspace
/// (<see cref="Workspace.DefaultTabs"/>) and lets the user delete them, and carries the per-workspace
/// opt-out from the last-session restore offer (<see cref="Workspace.SuppressSessionRestore"/>). Defining a
/// tabset happens elsewhere (right-click the workspace icon → "Use Tabset as Default"); this page only
/// prunes. Edits an in-memory copy of the list and writes it back to the live workspace + disk on
/// <see cref="Apply"/>, so closing the panel without applying discards the deletions — matching the
/// panel's per-section Apply model.
/// </summary>
public partial class WorkspaceDefaultTabsControl : UserControl, IConfigChangeTracker, ICustomConfigApply
{
    /// <summary>One list row. <see cref="Source"/> is the live descriptor kept for the surviving set on Apply.</summary>
    public sealed class TabRow
    {
        public required DefaultTabDescriptor Source  { get; init; }
        public required string               Display { get; init; }
    }

    public ObservableCollection<TabRow> Rows { get; } = [];

    private Workspace? _target;
    private bool _dirty;
    private bool _loading;   // true while LoadRows sets the controls, so that isn't taken for an edit

    public WorkspaceDefaultTabsControl()
    {
        InitializeComponent();
        RowsList.ItemsSource = Rows;
        DataContextChanged += OnTargetChanged;
    }

    private void OnTargetChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Workspace p) return;
        _target = p;
        LoadRows();
    }

    private void LoadRows()
    {
        Rows.Clear();
        if (_target is not null)
        {
            bool split = _target.DefaultTabs.Any(t => t.Pane == 1);   // only tag panes when a split was captured
            foreach (var t in _target.DefaultTabs)
                Rows.Add(new TabRow { Source = t, Display = Describe(t, split) });
        }
        UpdateEmptyState();

        // Set without going through the handler: loading the page is not an edit.
        _loading = true;
        NoSessionRestoreCheck.IsChecked = _target?.SuppressSessionRestore ?? false;
        _loading = false;
    }

    private static string Describe(DefaultTabDescriptor t, bool split)
    {
        var title = string.IsNullOrWhiteSpace(t.Title) ? t.PageKind : t.Title!;
        return split ? $"{title}  ·  {(t.Pane == 1 ? "Right pane" : "Left pane")}" : title;
    }

    private void UpdateEmptyState()
        => EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TabRow row }) return;
        Rows.Remove(row);
        _dirty = true;
        UpdateEmptyState();
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The session-restore opt-out. Held on the page until Apply, like a removed row.</summary>
    private void NoSessionRestore_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── IConfigChangeTracker: enables the panel's Apply button once a row is removed or the toggle moves ──
    public bool HasChanges => _dirty;
    public event EventHandler? HasChangesChanged;

    // ── ICustomConfigApply: commit the pruned list + the opt-out to the live workspace + disk ──
    public void Apply()
    {
        if (_target is null || !_dirty) return;
        _target.DefaultTabs           = Rows.Select(r => r.Source).ToList();
        _target.SuppressSessionRestore = NoSessionRestoreCheck.IsChecked == true;

        // Opting out drops a session already recorded, so applying it is the end of the offer rather
        // than the start of waiting for the next close to clear it.
        if (_target.SuppressSessionRestore) _target.LastSessionTabs = null;

        WorkspaceManager.Instance.SaveWorkspaces();
        _dirty = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }
}
