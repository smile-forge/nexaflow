using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.WindowsFileSystem.Controls;

/// <summary>
/// Default Actions Options tab. A list of configured per-extension double-click overrides, with an Add/Edit
/// panel that searches an extension's registered actions (built-in viewers, external apps, Windows shell
/// verbs) and lets the user confirm one. Edits accumulate in memory and are committed together on the
/// Options Save (<see cref="Apply"/> → <see cref="DefaultActionsConfig"/> + <see cref="DefaultActionRegistry"/>).
/// </summary>
public partial class DefaultActionsEditorControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    /// <summary>One selectable row in the candidate list; <see cref="Kind"/> null = "Automatic".</summary>
    public sealed class CandidateVm
    {
        public DefaultActionKind? Kind     { get; init; }
        public string            TargetId  { get; init; } = string.Empty;
        public string            Label     { get; init; } = string.Empty;
        public string            Group     { get; init; } = string.Empty;
        public bool              HasGroup  => !string.IsNullOrEmpty(Group);
    }

    // Which view is showing: the overrides list (false) or the add/edit panel (true).
    public static readonly DependencyProperty IsAddingProperty =
        DependencyProperty.Register(nameof(IsAdding), typeof(bool),
            typeof(DefaultActionsEditorControl), new PropertyMetadata(false));

    public bool IsAdding
    {
        get => (bool)GetValue(IsAddingProperty);
        private set => SetValue(IsAddingProperty, value);
    }

    // Confirm is enabled once a (non-empty) extension is entered.
    public static readonly DependencyProperty CanConfirmProperty =
        DependencyProperty.Register(nameof(CanConfirm), typeof(bool),
            typeof(DefaultActionsEditorControl), new PropertyMetadata(false));

    public bool CanConfirm
    {
        get => (bool)GetValue(CanConfirmProperty);
        private set => SetValue(CanConfirmProperty, value);
    }

    // True when no overrides are configured (drives the empty-state hint / hides the list).
    public static readonly DependencyProperty HasNoOverridesProperty =
        DependencyProperty.Register(nameof(HasNoOverrides), typeof(bool),
            typeof(DefaultActionsEditorControl), new PropertyMetadata(true));

    public bool HasNoOverrides
    {
        get => (bool)GetValue(HasNoOverridesProperty);
        private set => SetValue(HasNoOverridesProperty, value);
    }

    private readonly ObservableCollection<DefaultActionOverride> _overrides  = [];
    private readonly ObservableCollection<CandidateVm>           _candidates = [];

    private string _currentExt = string.Empty;   // bare, no dot — the extension being added/edited
    private bool   _syncing;
    private bool   _hasChanges;

    public bool HasChanges => _hasChanges;
    public event EventHandler? HasChangesChanged;

    public DefaultActionsEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        _overrides.CollectionChanged += OnOverridesChanged;
        CandidateList.ItemsSource = _candidates;
        SummaryList.ItemsSource    = _overrides;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DefaultActionsConfig cfg) return;

        _syncing = true;
        try
        {
            _overrides.Clear();
            foreach (var o in cfg.Overrides)
                _overrides.Add(new DefaultActionOverride
                {
                    Extension = o.Extension, Kind = o.Kind, TargetId = o.TargetId, Label = o.Label
                });
        }
        finally { _syncing = false; }

        IsAdding    = false;
        _hasChanges = false;
    }

    // ── List view ──────────────────────────────────────────────────────────────

    private void Add_Click(object sender, RoutedEventArgs e) => EnterAddMode(null);

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DefaultActionOverride o }) EnterAddMode(o);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DefaultActionOverride o }) return;
        _overrides.Remove(o);
        MarkDirty();
    }

    // ── Add / edit panel ─────────────────────────────────────────────────────────

    private void EnterAddMode(DefaultActionOverride? edit)
    {
        _syncing = true;
        try
        {
            IsAdding = true;
            AddTitle.Text = edit is null
                ? "Add a default action"
                : $"Edit default for .{NormalizeBare(edit.Extension)}";

            ExtBox.Text = edit is null ? string.Empty : NormalizeBare(edit.Extension);
            RebuildCandidates();   // syncing → doesn't touch selection

            CandidateVm? pick = edit is not null
                ? _candidates.FirstOrDefault(v => v.Kind == edit.Kind && v.TargetId == edit.TargetId)
                : null;
            CandidateList.SelectedItem = pick ?? _candidates.FirstOrDefault(v => v.Kind is null);
        }
        finally { _syncing = false; }

        ExtBox.Focus();
        ExtBox.SelectAll();
    }

    // Typing an extension refreshes (and thereby clears the previous) candidate list — live search.
    private void ExtBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        RebuildCandidates();
    }

    private void RebuildCandidates()
    {
        _currentExt = NormalizeBare(ExtBox.Text);

        _candidates.Clear();
        _candidates.Add(new CandidateVm { Kind = null, Label = "Automatic (Nexaflow default)" });

        if (!string.IsNullOrEmpty(_currentExt))
            foreach (var c in DefaultActionResolver.CandidatesFor(_currentExt))
                _candidates.Add(new CandidateVm { Kind = c.Kind, TargetId = c.TargetId, Label = c.Label, Group = c.Group });

        CanConfirm = !string.IsNullOrEmpty(_currentExt);
        CurrentExtLabel.Text = string.IsNullOrEmpty(_currentExt)
            ? "Enter an extension above."
            : $"Double-clicking a .{_currentExt} file will:";

        // Live edits default to Automatic; edit-mode preselection is set by EnterAddMode (while syncing).
        if (!_syncing)
            CandidateList.SelectedItem = _candidates.FirstOrDefault(v => v.Kind is null);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentExt)) return;

        var existing = FindOverride(_currentExt);
        if (existing is not null) _overrides.Remove(existing);

        if (CandidateList.SelectedItem is CandidateVm { Kind: DefaultActionKind kind } vm)   // not "Automatic"
            _overrides.Add(new DefaultActionOverride
            {
                Extension = _currentExt, Kind = kind, TargetId = vm.TargetId, Label = vm.Label
            });

        MarkDirty();
        IsAdding = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => IsAdding = false;

    // ── ICustomConfigApply ─────────────────────────────────────────────────────

    public void Apply()
    {
        if (DataContext is not DefaultActionsConfig cfg) return;
        cfg.Overrides = _overrides.ToList();
        DefaultActionRegistry.Instance.Update(cfg);
        _hasChanges = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void OnOverridesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => HasNoOverrides = _overrides.Count == 0;

    private DefaultActionOverride? FindOverride(string bareExt) =>
        _overrides.FirstOrDefault(o => string.Equals(NormalizeBare(o.Extension), bareExt, StringComparison.OrdinalIgnoreCase));

    private void MarkDirty()
    {
        if (_syncing || _hasChanges) return;
        _hasChanges = true;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizeBare(string ext)
    {
        var e = ext.Trim();
        if (e.StartsWith("*.")) e = e[2..];
        return e.TrimStart('.').ToLowerInvariant();
    }
}
