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

public partial class ExternalAppsEditorControl : UserControl, ICustomConfigApply, IConfigChangeTracker, IConfigValidation, IShellAware
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

    // Collapsible-section state (advanced fields collapsed; matching rules shown by default).
    public static readonly DependencyProperty AdvancedExpandedProperty =
        DependencyProperty.Register(nameof(AdvancedExpanded), typeof(bool),
            typeof(ExternalAppsEditorControl), new PropertyMetadata(false));

    public bool AdvancedExpanded
    {
        get => (bool)GetValue(AdvancedExpandedProperty);
        set => SetValue(AdvancedExpandedProperty, value);
    }

    public static readonly DependencyProperty RulesExpandedProperty =
        DependencyProperty.Register(nameof(RulesExpanded), typeof(bool),
            typeof(ExternalAppsEditorControl), new PropertyMetadata(true));

    public bool RulesExpanded
    {
        get => (bool)GetValue(RulesExpandedProperty);
        set => SetValue(RulesExpandedProperty, value);
    }

    // ── Match-criterion row (Type + Value) ───────────────────────────────────

    public sealed class CriterionRow : INotifyPropertyChanged
    {
        // External apps match by file extension glob or full-path glob; the richer FileMap
        // criteria types (PerceivedType/ContentType/MagicNumber) aren't honored for external apps.
        public static IReadOnlyList<string> TypeOptions { get; } =
            [nameof(CriteriaType.Extension), nameof(CriteriaType.PathPattern)];

        private string _typeName = nameof(CriteriaType.Extension);
        private string _value    = string.Empty;

        public string TypeName { get => _typeName; set { _typeName = value; Raise(nameof(TypeName)); Raise(nameof(IsValid)); } }
        public string Value    { get => _value;    set { _value = value;    Raise(nameof(Value));    Raise(nameof(IsValid)); } }

        /// <summary>False when the pattern can't work for the chosen type (rings the field red).</summary>
        public bool IsValid => CriterionValidity.IsValid(_typeName, _value);

        public FileSelectionCriteria ToCriteria() => new()
        {
            Type  = Enum.TryParse<CriteriaType>(_typeName, out var t) ? t : CriteriaType.Extension,
            Value = _value,
        };

        public static CriterionRow From(FileSelectionCriteria c) => new()
        {
            TypeName = c.Type.ToString(),
            Value    = c.Value,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ── Row view-model ───────────────────────────────────────────────────────

    public sealed class AppRow : INotifyPropertyChanged
    {
        public static IReadOnlyList<string> MultiFileOptions { get; } =
            Enum.GetNames<MultiFileMode>();

        /// <summary>Stable identity carried through edits so Default Action overrides keep referencing
        /// this app. Set on creation / import / load; a new one is minted on save if still empty.</summary>
        public string Id { get; set; } = string.Empty;

        private string _displayName = string.Empty;
        private string _applicationPath = string.Empty;
        private string _arguments = string.Empty;
        private string _workingDirectory = string.Empty;
        private string _iconPath = string.Empty;
        private string _multiFileName = nameof(MultiFileMode.SingleFileOnly);

        /// <summary>The file-match rules (OR-ed). The single source of truth for matching.</summary>
        public ObservableCollection<CriterionRow> CriteriaRows { get; } = [];

        public AppRow() => CriteriaRows.CollectionChanged += OnCriteriaChanged;

        public string DisplayName      { get => _displayName;      set => Set(ref _displayName, value); }
        public string ApplicationPath  { get => _applicationPath;  set => Set(ref _applicationPath, value); }
        public string Arguments        { get => _arguments;        set => Set(ref _arguments, value); }
        public string WorkingDirectory { get => _workingDirectory; set => Set(ref _workingDirectory, value); }
        public string IconPath         { get => _iconPath;         set => Set(ref _iconPath, value); }
        public string MultiFileName    { get => _multiFileName;    set => Set(ref _multiFileName, value); }

        public bool   HasCriteria  => CriteriaRows.Count > 0;
        /// <summary>List-row subtitle summarising the match rules.</summary>
        public string ListLabel => CriteriaRows.Count == 0
            ? "(no match rules)"
            : string.Join(", ", CriteriaRows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => r.Value));

        public ExternalAppDefinition ToDefinition() => new()
        {
            Id               = string.IsNullOrEmpty(Id) ? Guid.NewGuid().ToString("N") : Id,
            Extension        = string.Empty,   // legacy field retired — matching is criteria-only
            DisplayName      = DisplayName,
            ApplicationPath  = ApplicationPath,
            Arguments        = Arguments,
            WorkingDirectory = WorkingDirectory,
            IconPath         = IconPath,
            MultiFile        = Enum.TryParse<MultiFileMode>(MultiFileName, out var mf)
                                ? mf : MultiFileMode.SingleFileOnly,
            Criteria         = CriteriaRows.Select(r => r.ToCriteria())
                                           .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                                           .ToList(),
        };

        public static AppRow FromDefinition(ExternalAppDefinition d)
        {
            var row = new AppRow
            {
                Id               = d.Id,
                DisplayName      = d.DisplayName,
                ApplicationPath  = d.ApplicationPath,
                Arguments        = d.Arguments,
                WorkingDirectory = d.WorkingDirectory,
                IconPath         = d.IconPath,
                MultiFileName    = d.MultiFile.ToString(),
            };

            if (d.Criteria.Count > 0)
                foreach (var c in d.Criteria) row.CriteriaRows.Add(CriterionRow.From(c));
            else if (!string.IsNullOrWhiteSpace(d.Extension))   // migrate the legacy single-extension field
                row.CriteriaRows.Add(new CriterionRow { TypeName = nameof(CriteriaType.Extension), Value = ToExtGlob(d.Extension) });

            return row;
        }

        /// <summary>Normalises a legacy extension ("slnx", ".slnx", "*.slnx", "*") to a glob criterion value.</summary>
        private static string ToExtGlob(string ext)
        {
            var e = ext.Trim();
            if (e is "*" or "*.*") return "*";
            if (e.StartsWith("*.")) return e;
            e = e.TrimStart('.');
            return e.Length == 0 ? "*" : "*." + e;
        }

        // Re-raise criteria edits as a row-level change so the editor's dirty tracking sees them
        // and the list subtitle refreshes.
        private void OnCriteriaChanged(object? s, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null) foreach (CriterionRow r in e.NewItems) r.PropertyChanged += OnCriterionChanged;
            if (e.OldItems is not null) foreach (CriterionRow r in e.OldItems) r.PropertyChanged -= OnCriterionChanged;
            RaiseCriteriaChanged();
        }
        private void OnCriterionChanged(object? s, PropertyChangedEventArgs e) => RaiseCriteriaChanged();
        private void RaiseCriteriaChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCriteria)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListLabel)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // ── Deep-link target ───────────────────────────────────────────────────────

    /// <summary>When set before the Options panel opens (via the file browser's "Modify" command),
    /// the editor selects the matching app row on load, then clears this. The reference matches an
    /// entry in the canonical <see cref="ExternalAppsConfig.Apps"/> (shared instance).</summary>
    public static ExternalAppDefinition? PendingSelect { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private readonly ObservableCollection<AppRow> _rows = [];
    private bool _suppressDirty;
    private bool _hasChanges;
    private IShellServices? _shell;

    public bool HasChanges => _hasChanges;
    public event EventHandler? HasChangesChanged;

    // ── ctor ─────────────────────────────────────────────────────────────────

    public ExternalAppsEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        _rows.CollectionChanged += OnRowsChanged;
    }

    // The Options / Configure host injects the shell so the registry toggle can raise a themed confirmation.
    public void AttachShell(IShellServices shell) => _shell = shell;

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

        // Deep-link from "Modify": select the requested app (rows are parallel to cfg.Apps).
        if (PendingSelect is not null)
        {
            int idx = cfg.Apps.IndexOf(PendingSelect);
            if (idx >= 0 && idx < _rows.Count) AppsList.SelectedItem = _rows[idx];
            PendingSelect = null;
        }
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
        var row = new AppRow { DisplayName = "New App", Id = Guid.NewGuid().ToString("N") };
        row.CriteriaRows.Add(new CriterionRow());   // one empty extension rule to fill in
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

    // ── Match-rule add / remove ───────────────────────────────────────────────

    private void AddCriterion_Click(object sender, RoutedEventArgs e)
        => SelectedApp?.CriteriaRows.Add(new CriterionRow());

    private void RemoveCriterion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CriterionRow row } && SelectedApp is not null)
            SelectedApp.CriteriaRows.Remove(row);
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

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) { MarkDirty(); RaiseValidity(); }
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) { MarkDirty(); RaiseValidity(); }

    private void MarkDirty()
    {
        if (_suppressDirty || _hasChanges) return;
        _hasChanges = true;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── IConfigValidation ──────────────────────────────────────────────────────

    /// <summary>Every match rule across every app must be valid for its type, else Save is blocked.</summary>
    public bool IsValid => _rows.All(r => r.CriteriaRows.All(c => c.IsValid));
    public event EventHandler? IsValidChanged;
    private void RaiseValidity() => IsValidChanged?.Invoke(this, EventArgs.Empty);

    // ── Registry toggle ──────────────────────────────────────────────────────

    private async void RegistryToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        // OnLoaded pushes cfg.UseRegistryMapping onto the bound ToggleButton; when the stored value is
        // false that programmatic flip raises Unchecked. Without this guard, merely opening Options while
        // registry handlers are off pops the dialog on load. Only react to a genuine user toggle.
        if (_suppressDirty) return;

        // Turning the toggle off just stops the live "Open with"/New-menu entries — fully reversible, so
        // there's no cancel-and-revert path; the only choice is whether to keep the current handlers by
        // importing them as editable External Apps. Native MessageBox is banned → themed confirmation
        // with "Import" / "Do not Import" captions. Either choice turns the handlers off.
        if (_shell is not null)
        {
            bool import = await _shell.ConfirmAsync(
                "Turn off Windows-registered handlers?",
                "The live \"Open with\" buttons and New-menu entries will stop appearing (re-enable any time).\n\n" +
                "Keep your current Windows \"Open with\" apps by importing them into the External Apps list as " +
                "editable buttons first?",
                confirmLabel: "Import", cancelLabel: "Do not Import");
            if (import)
                await ImportRegistryHandlersAsync();
        }

        if (DataContext is ExternalAppsConfig cfg) cfg.UseRegistryMapping = false;
        MarkDirty();
    }

    /// <summary>Enumerates HKCR "open" handlers (background) and merges them into the app list, deduping
    /// by executable — a slow one-shot registry sweep, so show a wait cursor.</summary>
    private async System.Threading.Tasks.Task ImportRegistryHandlersAsync()
    {
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            var defs = await System.Threading.Tasks.Task.Run(RegistryHandlerImport.EnumerateOpenHandlers);
            MergeImported(defs);
        }
        finally { System.Windows.Input.Mouse.OverrideCursor = null; }
    }

    /// <summary>Adds each imported definition as a new app, or unions its extensions into an existing app
    /// with the same executable path.</summary>
    private void MergeImported(IReadOnlyList<ExternalAppDefinition> defs)
    {
        foreach (var def in defs)
        {
            var existing = _rows.FirstOrDefault(r =>
                string.Equals(r.ApplicationPath, def.ApplicationPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                foreach (var c in def.Criteria)
                {
                    bool already = existing.CriteriaRows.Any(cr =>
                        cr.TypeName == nameof(CriteriaType.Extension) &&
                        string.Equals(cr.Value, c.Value, StringComparison.OrdinalIgnoreCase));
                    if (!already)
                        existing.CriteriaRows.Add(new CriterionRow
                        {
                            TypeName = nameof(CriteriaType.Extension),
                            Value    = c.Value,
                        });
                }
            }
            else
            {
                var row = AppRow.FromDefinition(def);
                row.PropertyChanged += OnRowPropertyChanged;
                _rows.Add(row);
            }
        }
        MarkDirty();
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
