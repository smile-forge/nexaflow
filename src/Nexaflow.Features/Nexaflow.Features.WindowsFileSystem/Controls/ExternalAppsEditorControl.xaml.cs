using Microsoft.Win32;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.WindowsFileSystem.Controls;

public partial class ExternalAppsEditorControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    // ── Dependency properties ────────────────────────────────────────────────

    public static readonly DependencyProperty UseRegistryMappingProperty =
        DependencyProperty.Register(nameof(UseRegistryMapping), typeof(bool),
            typeof(ExternalAppsEditorControl), new PropertyMetadata(true, OnUseRegistryMappingChanged));

    public bool UseRegistryMapping
    {
        get => (bool)GetValue(UseRegistryMappingProperty);
        set => SetValue(UseRegistryMappingProperty, value);
    }

    public static readonly DependencyProperty SelectedAppProperty =
        DependencyProperty.Register(nameof(SelectedApp), typeof(AppRow),
            typeof(ExternalAppsEditorControl), new PropertyMetadata(null, OnSelectedAppChanged));

    public AppRow? SelectedApp
    {
        get => (AppRow?)GetValue(SelectedAppProperty);
        set => SetValue(SelectedAppProperty, value);
    }

    public static readonly DependencyProperty HasSelectedAppProperty =
        DependencyProperty.Register(nameof(HasSelectedApp), typeof(bool),
            typeof(ExternalAppsEditorControl), new PropertyMetadata(false));

    public bool HasSelectedApp
    {
        get => (bool)GetValue(HasSelectedAppProperty);
        private set => SetValue(HasSelectedAppProperty, value);
    }

    // ── Row view-model ───────────────────────────────────────────────────────

    public sealed class AppRow : INotifyPropertyChanged
    {
        public static IReadOnlyList<string> MultiFileOptions { get; } =
            Enum.GetNames<MultiFileMode>();

        private string _extension = string.Empty;
        private string _displayName = string.Empty;
        private string _applicationPath = string.Empty;
        private string _arguments = string.Empty;
        private string _workingDirectory = string.Empty;
        private string _iconPath = string.Empty;
        private string _multiFileName = nameof(MultiFileMode.SingleFileOnly);

        public string Extension        { get => _extension;        set => Set(ref _extension, value); }
        public string DisplayName      { get => _displayName;      set => Set(ref _displayName, value); }
        public string ApplicationPath  { get => _applicationPath;  set => Set(ref _applicationPath, value); }
        public string Arguments        { get => _arguments;        set => Set(ref _arguments, value); }
        public string WorkingDirectory { get => _workingDirectory; set => Set(ref _workingDirectory, value); }
        public string IconPath         { get => _iconPath;         set => Set(ref _iconPath, value); }
        public string MultiFileName    { get => _multiFileName;    set => Set(ref _multiFileName, value); }

        public ExternalAppDefinition ToDefinition() => new()
        {
            Extension        = Extension,
            DisplayName      = DisplayName,
            ApplicationPath  = ApplicationPath,
            Arguments        = Arguments,
            WorkingDirectory = WorkingDirectory,
            IconPath         = IconPath,
            MultiFile        = Enum.TryParse<MultiFileMode>(MultiFileName, out var mf)
                                ? mf : MultiFileMode.SingleFileOnly,
        };

        public static AppRow FromDefinition(ExternalAppDefinition d) => new()
        {
            Extension        = d.Extension,
            DisplayName      = d.DisplayName,
            ApplicationPath  = d.ApplicationPath,
            Arguments        = d.Arguments,
            WorkingDirectory = d.WorkingDirectory,
            IconPath         = d.IconPath,
            MultiFileName    = d.MultiFile.ToString(),
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // ── State ────────────────────────────────────────────────────────────────

    private readonly ObservableCollection<AppRow> _rows = [];
    private bool _suppressDirty;
    private bool _hasChanges;

    public bool HasChanges => _hasChanges;
    public event EventHandler? HasChangesChanged;

    // ── ctor ─────────────────────────────────────────────────────────────────

    public ExternalAppsEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        _rows.CollectionChanged += OnRowsChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExternalAppsConfig cfg) return;

        _suppressDirty = true;
        try
        {
            UseRegistryMapping = cfg.UseRegistryMapping;
            _rows.Clear();
            foreach (var def in cfg.Apps)
            {
                var row = AppRow.FromDefinition(def);
                row.PropertyChanged += OnRowPropertyChanged;
                _rows.Add(row);
            }
        }
        finally { _suppressDirty = false; }

        AppsList.ItemsSource = _rows;
        _hasChanges = false;
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private void AppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedApp = AppsList.SelectedItem as AppRow;
    }

    private static void OnSelectedAppChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ExternalAppsEditorControl c)
            c.HasSelectedApp = e.NewValue is not null;
    }

    // ── Add / remove ─────────────────────────────────────────────────────────

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var row = new AppRow { DisplayName = "New App", Extension = "*" };
        row.PropertyChanged += OnRowPropertyChanged;
        _rows.Add(row);
        AppsList.SelectedItem = row;
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (AppsList.SelectedItem is AppRow row)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
            _rows.Remove(row);
        }
    }

    // ── Browse buttons ───────────────────────────────────────────────────────

    private void BrowseAppPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Executables (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) SelectedApp.ApplicationPath = dlg.FileName;
    }

    private void BrowseWorkingDir_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null) return;
        var dlg = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(SelectedApp.WorkingDirectory))
            dlg.InitialDirectory = SelectedApp.WorkingDirectory;
        if (dlg.ShowDialog() == true) SelectedApp.WorkingDirectory = dlg.FolderName;
    }

    private void BrowseIconPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Icons (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) SelectedApp.IconPath = dlg.FileName;
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

    // ── Registry toggle ──────────────────────────────────────────────────────

    private void RegistryToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will convert all registry-derived mappings to user-managed mappings. " +
            "They will no longer be updated automatically.\n\nProceed?",
            "Disable Registry Mapping",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            FileMapManager.Instance.ConvertRegistryMappingsToUser();
            if (DataContext is ExternalAppsConfig cfg) cfg.UseRegistryMapping = false;
            MarkDirty();
        }
        else
        {
            UseRegistryMapping = true;
            if (DataContext is ExternalAppsConfig cfg) cfg.UseRegistryMapping = true;
        }
    }

    private static void OnUseRegistryMappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ExternalAppsEditorControl ctrl && ctrl.DataContext is ExternalAppsConfig cfg)
        {
            cfg.UseRegistryMapping = (bool)e.NewValue;
            if (!ctrl._suppressDirty) ctrl.MarkDirty();
        }
    }

    // ── ICustomConfigApply ───────────────────────────────────────────────────

    public void Apply()
    {
        if (DataContext is not ExternalAppsConfig cfg) return;
        cfg.UseRegistryMapping = UseRegistryMapping;
        cfg.Apps = _rows.Select(r => r.ToDefinition()).ToList();
        ExternalAppRegistry.Instance.Update(cfg);
        // Registry-derived ShellNew entries follow the same toggle: start/stop the HKCR scan.
        ShellNewRegistry.Instance.Update(cfg.UseRegistryMapping);
        _hasChanges = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }
}
