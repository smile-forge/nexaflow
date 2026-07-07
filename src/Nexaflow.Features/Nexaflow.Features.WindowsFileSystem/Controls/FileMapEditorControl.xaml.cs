using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.WindowsFileSystem.Controls;

public partial class FileMapEditorControl : UserControl, ICustomConfigApply, IConfigValidation
{
    // ── Dependency properties ────────────────────────────────────────────────

    public static readonly DependencyProperty SelectedMappingProperty =
        DependencyProperty.Register(nameof(SelectedMapping), typeof(ExperienceMapping),
            typeof(FileMapEditorControl), new PropertyMetadata(null, OnSelectedMappingChanged));

    public ExperienceMapping? SelectedMapping
    {
        get => (ExperienceMapping?)GetValue(SelectedMappingProperty);
        set => SetValue(SelectedMappingProperty, value);
    }

    public static readonly DependencyProperty HasSelectedMappingProperty =
        DependencyProperty.Register(nameof(HasSelectedMapping), typeof(bool),
            typeof(FileMapEditorControl), new PropertyMetadata(false));

    public bool HasSelectedMapping
    {
        get => (bool)GetValue(HasSelectedMappingProperty);
        private set => SetValue(HasSelectedMappingProperty, value);
    }

    public static readonly DependencyProperty CanResetToDefaultProperty =
        DependencyProperty.Register(nameof(CanResetToDefault), typeof(bool),
            typeof(FileMapEditorControl), new PropertyMetadata(false));

    /// <summary>True when the selected experience has a bundled default <em>and</em> its current criteria
    /// differ from it — i.e. a reset would actually change something. Drives the "Reset to Default" button's
    /// visibility so it doesn't show when there's nothing to revert.</summary>
    public bool CanResetToDefault
    {
        get => (bool)GetValue(CanResetToDefaultProperty);
        private set => SetValue(CanResetToDefaultProperty, value);
    }

    public static readonly DependencyProperty ResetConfirmPendingProperty =
        DependencyProperty.Register(nameof(ResetConfirmPending), typeof(bool),
            typeof(FileMapEditorControl), new PropertyMetadata(false));

    /// <summary>True while the inline "reset to default?" confirm step is showing. Inline rather than a shell
    /// confirmation overlay, which can't stack above the Options panel that hosts this control.</summary>
    public bool ResetConfirmPending
    {
        get => (bool)GetValue(ResetConfirmPendingProperty);
        private set => SetValue(ResetConfirmPendingProperty, value);
    }

    /// <summary>When set before the Options panel opens (via the file browser's "Modify" command),
    /// the editor selects this experience on load, then clears it.</summary>
    public static string? PendingExperienceId { get; set; }

    // ── Tree node model ──────────────────────────────────────────────────────

    private sealed class ExperienceNode
    {
        public string                               Segment  { get; }
        public string                               FullId   { get; }
        public ObservableCollection<ExperienceNode> Children { get; } = [];

        public ExperienceNode(string segment, string fullId)
        {
            Segment = segment;
            FullId  = fullId;
        }
    }

    // ── Criteria row view model ──────────────────────────────────────────────

    public sealed class CriterionRow : INotifyPropertyChanged
    {
        public static IReadOnlyList<string> TypeOptions { get; } =
            Enum.GetNames<CriteriaType>();

        private string _typeName = CriteriaType.Extension.ToString();
        private string _value    = string.Empty;

        public string TypeName
        {
            get => _typeName;
            set { _typeName = value; PropertyChanged?.Invoke(this, new(nameof(TypeName))); PropertyChanged?.Invoke(this, new(nameof(IsValid))); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); PropertyChanged?.Invoke(this, new(nameof(IsValid))); }
        }

        /// <summary>False when the value can't work for the chosen type (rings the field red).</summary>
        public bool IsValid => CriterionValidity.IsValid(_typeName, _value);

        public FileSelectionCriteria ToCriteria() => new()
        {
            Type  = Enum.Parse<CriteriaType>(_typeName),
            Value = _value,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    // Rows for the currently-selected experience. Bound to the criteria ItemsControl.
    private readonly ObservableCollection<CriterionRow> _criteria = [];

    public FileMapEditorControl()
    {
        InitializeComponent();
        _criteria.CollectionChanged += OnCriteriaChanged;
        CriteriaItems.ItemsSource = _criteria;
        Loaded += OnLoaded;
    }

    // ── IConfigValidation ──────────────────────────────────────────────────────

    public bool IsValid => _criteria.All(c => c.IsValid);
    public event EventHandler? IsValidChanged;
    private void RaiseValidity() => IsValidChanged?.Invoke(this, EventArgs.Empty);

    private void OnCriteriaChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (CriterionRow r in e.NewItems) r.PropertyChanged += OnCriterionChanged;
        if (e.OldItems is not null) foreach (CriterionRow r in e.OldItems) r.PropertyChanged -= OnCriterionChanged;
        RaiseValidity();
        UpdateCanReset();
    }
    private void OnCriterionChanged(object? s, PropertyChangedEventArgs e) { RaiseValidity(); UpdateCanReset(); }

    /// <summary>Shows the "Reset to Default" button only when the current (live) criteria differ from the
    /// selected experience's bundled default. Blank-value rows are ignored (they don't affect matching).</summary>
    private void UpdateCanReset()
    {
        var def = SelectedMapping is null
            ? null
            : FileMapManager.Instance.GetBundledDefault(SelectedMapping.ExperienceId);
        CanResetToDefault = def is not null && !CurrentCriteriaMatch(def);
    }

    private bool CurrentCriteriaMatch(ExperienceMapping def)
    {
        static IEnumerable<string> Normalize(IEnumerable<(string Type, string Value)> items) =>
            items.Where(i => !string.IsNullOrWhiteSpace(i.Value))
                 .Select(i => i.Type + "|" + i.Value.Trim().ToLowerInvariant())
                 .OrderBy(s => s, StringComparer.Ordinal);

        return Normalize(_criteria.Select(r => (r.TypeName, r.Value)))
            .SequenceEqual(Normalize(def.Criteria.Select(c => (c.Type.ToString(), c.Value))));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateTree();

        // Deep-link from "Modify": show the requested experience's criteria, then highlight its
        // tree node once the containers exist.
        if (PendingExperienceId is { Length: > 0 } expId)
        {
            PendingExperienceId = null;
            SelectedMapping = FileMapManager.Instance.GetMapping(expId)
                ?? new ExperienceMapping { ExperienceId = expId, Source = MappingSource.User };
            Dispatcher.BeginInvoke(
                new Action(() => { try { SelectTreeNode(ExperienceTree, expId); } catch { } }),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>Best-effort: finds and selects the <see cref="TreeViewItem"/> for <paramref name="fullId"/>.</summary>
    private static bool SelectTreeNode(ItemsControl parent, string fullId)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem tvi) continue;
            if (item is ExperienceNode node &&
                string.Equals(node.FullId, fullId, StringComparison.OrdinalIgnoreCase))
            {
                tvi.IsSelected = true;
                tvi.BringIntoView();
                return true;
            }
            tvi.IsExpanded = true;
            tvi.UpdateLayout();
            if (SelectTreeNode(tvi, fullId)) return true;
        }
        return false;
    }

    private void PopulateTree()
    {
        ExperienceTree.Items.Clear();
        var ids = FileMapManager.Instance.GetAllExperienceIds();

        // "/" is always the single root — displayed as "File"
        var rootNode = new ExperienceNode("File", "/");
        ExperienceTree.Items.Add(rootNode);

        foreach (var id in ids)
        {
            if (id == "/") continue;

            var parts = id.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            // All IDs are descendants of "/", so start from root's children
            ObservableCollection<ExperienceNode> parent = rootNode.Children;
            string built = string.Empty;

            foreach (var part in parts)
            {
                built = built + "/" + part;
                var node = parent.FirstOrDefault(n => n.Segment == part);
                if (node is null)
                {
                    node = new ExperienceNode(part, built);
                    parent.Add(node);
                }
                parent = node.Children;
            }
        }
    }

    private void ExperienceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SaveCurrentMapping();

        if (e.NewValue is not ExperienceNode node) { SelectedMapping = null; return; }

        var mapping = FileMapManager.Instance.GetMapping(node.FullId)
                   ?? new ExperienceMapping { ExperienceId = node.FullId, Source = MappingSource.User };
        SelectedMapping = mapping;
    }

    private static void OnSelectedMappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FileMapEditorControl ctrl) return;
        ctrl.HasSelectedMapping = e.NewValue is not null;
        ctrl.RefreshCriteriaList();
    }

    private void RefreshCriteriaList()
    {
        _criteria.Clear();
        ResetConfirmPending = false;   // a pending confirm doesn't carry across experiences
        if (SelectedMapping is null) { CanResetToDefault = false; return; }

        ExperienceIdLabel.Text   = SelectedMapping.ExperienceId;
        ExperienceDescLabel.Text = SelectedMapping.Source == MappingSource.Registry
            ? "(registry-derived)"
            : string.Empty;

        foreach (var c in SelectedMapping.Criteria)
            _criteria.Add(new CriterionRow { TypeName = c.Type.ToString(), Value = c.Value });

        UpdateCanReset();   // reflect the just-loaded (persisted) state
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private void SaveCurrentMapping()
    {
        if (SelectedMapping is null) return;
        SelectedMapping.Criteria.Clear();
        foreach (var row in _criteria)
            SelectedMapping.Criteria.Add(row.ToCriteria());
        SelectedMapping.Source = MappingSource.User;
        FileMapManager.Instance.SaveMapping(SelectedMapping);
    }

    // Called by OptionsViewModel when the user clicks Save in the Options panel.
    public void Apply() => SaveCurrentMapping();

    // ── Criterion buttons ────────────────────────────────────────────────────

    private void AddCriterion_Click(object sender, RoutedEventArgs e)
        => _criteria.Add(new CriterionRow());

    // "Reset to Default" shows an inline confirm step (below) rather than a shell confirmation overlay,
    // which can't render above the Options panel that hosts this control.
    private void ResetToDefault_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMapping is not null) ResetConfirmPending = true;
    }

    private void ResetCancel_Click(object sender, RoutedEventArgs e) => ResetConfirmPending = false;

    /// <summary>Restores the selected experience's bundled default criteria and reloads the editor rows.
    /// Persists immediately — like leaving a node, this control saves eagerly.</summary>
    private void ResetConfirm_Click(object sender, RoutedEventArgs e)
    {
        ResetConfirmPending = false;
        if (SelectedMapping is null) return;
        var id = SelectedMapping.ExperienceId;
        if (FileMapManager.Instance.ResetToDefault(id) is null) return;
        // Reload the freshly-reset mapping so the criteria rows refresh (and aren't re-saved stale).
        SelectedMapping = FileMapManager.Instance.GetMapping(id);
    }

    private void RemoveCriterion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CriterionRow row })
            _criteria.Remove(row);
    }
}
