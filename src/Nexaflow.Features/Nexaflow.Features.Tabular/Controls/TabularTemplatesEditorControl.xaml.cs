using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.Templates;

namespace Nexaflow.Features.Tabular.Controls;

/// <summary>
/// Options-panel editor for <see cref="TabularTemplatesConfig"/>: a list of saved templates with
/// inline rename and per-row delete. Edits the live config on <see cref="Apply"/> (Options Save).
/// </summary>
public partial class TabularTemplatesEditorControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    public sealed class Row : INotifyPropertyChanged
    {
        private string _name;

        public Row(TabularTemplate template)
        {
            Template = template;
            _name    = template.Name;
            int from = template.FieldCount;
            int to   = template.FinalHeaders?.Length ?? from;
            ColumnsSummary = from == to ? $"{from} columns" : $"{from} → {to} columns";
        }

        public TabularTemplate Template { get; }
        public string ScopeSummary   => Template.ScopeSummary;
        public string ColumnsSummary { get; }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); Dirty?.Invoke(); } }
        }

        /// <summary>Raised when an editable field changes, so the host can flag the section dirty.</summary>
        public Action? Dirty { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public static readonly DependencyProperty TemplateCountProperty =
        DependencyProperty.Register(nameof(TemplateCount), typeof(int),
            typeof(TabularTemplatesEditorControl), new PropertyMetadata(0));

    public int TemplateCount
    {
        get => (int)GetValue(TemplateCountProperty);
        private set => SetValue(TemplateCountProperty, value);
    }

    private readonly ObservableCollection<Row> _rows = [];
    private bool _hasChanges;

    public bool HasChanges => _hasChanges;
    public event EventHandler? HasChangesChanged;

    public TabularTemplatesEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TabularTemplatesConfig cfg) return;

        _rows.Clear();
        foreach (var t in cfg.Templates)
            _rows.Add(new Row(t) { Dirty = MarkDirty });

        RowsHost.ItemsSource = _rows;
        TemplateCount = _rows.Count;
        _hasChanges   = false;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Row row })
        {
            _rows.Remove(row);
            TemplateCount = _rows.Count;
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (_hasChanges) return;
        _hasChanges = true;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Apply()
    {
        if (DataContext is not TabularTemplatesConfig cfg) return;

        foreach (var row in _rows)
            row.Template.Name = row.Name.Trim();
        cfg.Templates = _rows.Select(r => r.Template).ToList();

        _hasChanges = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }
}
