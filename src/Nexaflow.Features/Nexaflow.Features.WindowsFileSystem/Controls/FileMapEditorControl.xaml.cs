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
    }
    private void OnCriterionChanged(object? s, PropertyChangedEventArgs e) => RaiseValidity();

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
        if (SelectedMapping is null) return;

        ExperienceIdLabel.Text   = SelectedMapping.ExperienceId;
        ExperienceDescLabel.Text = SelectedMapping.Source == MappingSource.Registry
            ? "(registry-derived)"
            : string.Empty;

        foreach (var c in SelectedMapping.Criteria)
            _criteria.Add(new CriterionRow { TypeName = c.Type.ToString(), Value = c.Value });
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

    private void RemoveCriterion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CriterionRow row })
            _criteria.Remove(row);
    }
}
