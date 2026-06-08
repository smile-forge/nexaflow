using Microsoft.Win32;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.WindowsFileSystem.Controls;

public partial class TemplatedCreateEditorControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    // ── Dependency properties ────────────────────────────────────────────────

    public static readonly DependencyProperty SelectedTemplateProperty =
        DependencyProperty.Register(nameof(SelectedTemplate), typeof(TemplateRow),
            typeof(TemplatedCreateEditorControl), new PropertyMetadata(null, OnSelectedTemplateChanged));

    public TemplateRow? SelectedTemplate
    {
        get => (TemplateRow?)GetValue(SelectedTemplateProperty);
        set => SetValue(SelectedTemplateProperty, value);
    }

    public static readonly DependencyProperty HasSelectedTemplateProperty =
        DependencyProperty.Register(nameof(HasSelectedTemplate), typeof(bool),
            typeof(TemplatedCreateEditorControl), new PropertyMetadata(false));

    public bool HasSelectedTemplate
    {
        get => (bool)GetValue(HasSelectedTemplateProperty);
        private set => SetValue(HasSelectedTemplateProperty, value);
    }

    // ── Row view-model ───────────────────────────────────────────────────────

    public sealed class TemplateRow : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _icon = "📄";
        private string _fileExtension = string.Empty;
        private string _sourcePath = string.Empty;
        private string _templateFileName = string.Empty;

        public string Name          { get => _name;          set => Set(ref _name, value); }
        public string Icon          { get => _icon;          set => Set(ref _icon, value); }
        public string FileExtension { get => _fileExtension; set => Set(ref _fileExtension, value); }
        public string SourcePath    { get => _sourcePath;    set => Set(ref _sourcePath, value); }

        public string TemplateFileName
        {
            get => _templateFileName;
            set { if (Set(ref _templateFileName, value)) OnPropertyChanged(nameof(HasStoredTemplate)); }
        }

        public bool HasStoredTemplate => !string.IsNullOrEmpty(_templateFileName);

        public TemplateDefinition ToDefinition() => new()
        {
            Name             = Name,
            Icon             = Icon,
            FileExtension    = FileExtension,
            SourcePath       = SourcePath,
            TemplateFileName = TemplateFileName,
        };

        public static TemplateRow FromDefinition(TemplateDefinition d) => new()
        {
            Name             = d.Name,
            Icon             = string.IsNullOrEmpty(d.Icon) ? "📄" : d.Icon,
            FileExtension    = d.FileExtension,
            SourcePath       = d.SourcePath,
            TemplateFileName = d.TemplateFileName,
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged(string? name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── State ────────────────────────────────────────────────────────────────

    private readonly ObservableCollection<TemplateRow> _rows = [];
    private bool _suppressDirty;
    private bool _hasChanges;

    public bool HasChanges => _hasChanges;
    public event EventHandler? HasChangesChanged;

    // ── ctor ─────────────────────────────────────────────────────────────────

    public TemplatedCreateEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        _rows.CollectionChanged += OnRowsChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TemplatedCreateConfig cfg) return;

        _suppressDirty = true;
        try
        {
            _rows.Clear();
            foreach (var def in cfg.Templates)
            {
                var row = TemplateRow.FromDefinition(def);
                row.PropertyChanged += OnRowPropertyChanged;
                _rows.Add(row);
            }
        }
        finally { _suppressDirty = false; }

        TemplatesList.ItemsSource = _rows;
        _hasChanges = false;
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private void TemplatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedTemplate = TemplatesList.SelectedItem as TemplateRow;
    }

    private static void OnSelectedTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TemplatedCreateEditorControl c)
            c.HasSelectedTemplate = e.NewValue is not null;
    }

    // ── Add / remove ─────────────────────────────────────────────────────────

    private void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        var row = new TemplateRow { Name = "New Template" };
        row.PropertyChanged += OnRowPropertyChanged;
        _rows.Add(row);
        TemplatesList.SelectedItem = row;
    }

    private void RemoveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplatesList.SelectedItem is TemplateRow row)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
            _rows.Remove(row);
        }
    }

    // ── Browse ───────────────────────────────────────────────────────────────

    private void BrowseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is null) return;
        var dlg = new OpenFileDialog { Filter = "All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;

        SelectedTemplate.SourcePath = dlg.FileName;
        if (string.IsNullOrWhiteSpace(SelectedTemplate.Name) ||
            SelectedTemplate.Name == "New Template")
            SelectedTemplate.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
        if (string.IsNullOrWhiteSpace(SelectedTemplate.FileExtension))
            SelectedTemplate.FileExtension = Path.GetExtension(dlg.FileName);
    }

    // ── Dirty tracking ───────────────────────────────────────────────────────

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkDirty();
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (_suppressDirty || _hasChanges) return;
        _hasChanges = true;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── ICustomConfigApply ───────────────────────────────────────────────────

    public void Apply()
    {
        if (DataContext is not TemplatedCreateConfig cfg) return;

        var defs = _rows.Select(r => r.ToDefinition()).ToList();
        TemplateStore.SaveTemplates(TemplatedCreateRegistry.Instance.TemplatesDir, defs);

        cfg.Templates = defs;
        TemplatedCreateRegistry.Instance.Update(cfg);

        // Reflect the stored names / cleared source paths back into the rows so a second
        // Save doesn't recopy.
        _suppressDirty = true;
        try
        {
            for (int i = 0; i < _rows.Count && i < defs.Count; i++)
            {
                _rows[i].TemplateFileName = defs[i].TemplateFileName;
                _rows[i].SourcePath       = defs[i].SourcePath;
            }
        }
        finally { _suppressDirty = false; }

        _hasChanges = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }
}
