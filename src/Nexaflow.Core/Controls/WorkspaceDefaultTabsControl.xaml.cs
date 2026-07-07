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
/// (<see cref="Profile.DefaultTabs"/>) and lets the user delete them. Defining a tabset happens elsewhere
/// (right-click the workspace icon → "Use Tabset as Default"); this page only prunes. Edits an in-memory
/// copy of the list and writes it back to the live profile + disk on <see cref="Apply"/>, so closing the
/// panel without applying discards the deletions — matching the panel's per-section Apply model.
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

    private Profile? _target;
    private bool _dirty;

    public WorkspaceDefaultTabsControl()
    {
        InitializeComponent();
        RowsList.ItemsSource = Rows;
        DataContextChanged += OnTargetChanged;
    }

    private void OnTargetChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Profile p) return;
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

    // ── IConfigChangeTracker: enables the panel's Apply button once a row is removed ──
    public bool HasChanges => _dirty;
    public event EventHandler? HasChangesChanged;

    // ── ICustomConfigApply: commit the pruned list to the live profile + disk ──
    public void Apply()
    {
        if (_target is null || !_dirty) return;
        _target.DefaultTabs = Rows.Select(r => r.Source).ToList();
        WorkspaceManager.Instance.SaveProfiles();
        _dirty = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }
}
