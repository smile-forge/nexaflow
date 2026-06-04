using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.WindowsFileSystem.Controls;

public partial class FileMapEditorControl : UserControl, ICustomConfigApply
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
            set { _typeName = value; PropertyChanged?.Invoke(this, new(nameof(TypeName))); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); }
        }

        public FileSelectionCriteria ToCriteria() => new()
        {
            Type  = Enum.Parse<CriteriaType>(_typeName),
            Value = _value,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    public FileMapEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => PopulateTree();

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
        CriteriaList.Items.Clear();
        if (SelectedMapping is null) return;

        ExperienceIdLabel.Text   = SelectedMapping.ExperienceId;
        ExperienceDescLabel.Text = SelectedMapping.Source == MappingSource.Registry
            ? "(registry-derived)"
            : string.Empty;

        foreach (var c in SelectedMapping.Criteria)
        {
            CriteriaList.Items.Add(new CriterionRow
            {
                TypeName = c.Type.ToString(),
                Value    = c.Value,
            });
        }
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private void SaveCurrentMapping()
    {
        if (SelectedMapping is null) return;
        SelectedMapping.Criteria.Clear();
        foreach (var row in CriteriaList.Items.OfType<CriterionRow>())
            SelectedMapping.Criteria.Add(row.ToCriteria());
        SelectedMapping.Source = MappingSource.User;
        FileMapManager.Instance.SaveMapping(SelectedMapping);
    }

    // Called by OptionsViewModel when the user clicks Save in the Options panel.
    public void Apply() => SaveCurrentMapping();

    // ── Criterion buttons ────────────────────────────────────────────────────

    private void AddCriterion_Click(object sender, RoutedEventArgs e)
    {
        CriteriaList.Items.Add(new CriterionRow());
    }

    private void RemoveCriterion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CriterionRow row)
            CriteriaList.Items.Remove(row);
    }
}
