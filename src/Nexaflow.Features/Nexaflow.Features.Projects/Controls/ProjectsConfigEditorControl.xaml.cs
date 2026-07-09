using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Visuals.Common.Theming;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Projects.Controls;

/// <summary>
/// The Projects config editor (a <see cref="CustomControlAttribute"/> target, so it replaces the default
/// property grid). Renders the enable toggle, the three project-folder locations (with themed folder
/// browse), and a reorderable list of backlog statuses — each row edits its label, colour (from the
/// shared <see cref="SwatchPalette"/>) and whether it is the terminal "cancelled" state. The list ORDER
/// is the forward-progression order. Working copies are held here and committed to the config only in
/// <see cref="Apply"/> (so cancelling Options discards edits).
/// </summary>
public partial class ProjectsConfigEditorControl
    : UserControl, ICustomConfigApply, IConfigChangeTracker, IConfigValidation, IShellAware
{
    private readonly ObservableCollection<StatusRow> _statuses = [];

    /// <summary>The shared swatch bank offered by each row's colour picker.</summary>
    public IReadOnlyList<SwatchOption> Swatches { get; } = SafeBuildSwatches();

    private ProjectsConfig? _config;
    private IShellServices? _shell;
    private bool _hasChanges;
    private bool _suppressDirty;

    public ProjectsConfigEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // The Configure/wizard host injects the shell so the browse buttons use the themed folder picker.
    public void AttachShell(IShellServices shell) => _shell = shell;

    private static IReadOnlyList<SwatchOption> SafeBuildSwatches()
    {
        try { return SwatchPalette.Build(); } catch { return []; }
    }

    // ── Working-copy scalars (bound via RelativeSource=self; committed in Apply) ──

    public static readonly DependencyProperty EnableProjectsProperty =
        DependencyProperty.Register(nameof(EnableProjects), typeof(bool),
            typeof(ProjectsConfigEditorControl), new PropertyMetadata(false, OnFieldChanged));

    public bool EnableProjects
    {
        get => (bool)GetValue(EnableProjectsProperty);
        set => SetValue(EnableProjectsProperty, value);
    }

    public static readonly DependencyProperty ProjectDirectoryProperty =
        DependencyProperty.Register(nameof(ProjectDirectory), typeof(string),
            typeof(ProjectsConfigEditorControl), new PropertyMetadata(string.Empty, OnFieldChanged));

    public string ProjectDirectory
    {
        get => (string)GetValue(ProjectDirectoryProperty);
        set => SetValue(ProjectDirectoryProperty, value);
    }

    public static readonly DependencyProperty ArchiveDirectoryProperty =
        DependencyProperty.Register(nameof(ArchiveDirectory), typeof(string),
            typeof(ProjectsConfigEditorControl), new PropertyMetadata(string.Empty, OnFieldChanged));

    public string ArchiveDirectory
    {
        get => (string)GetValue(ArchiveDirectoryProperty);
        set => SetValue(ArchiveDirectoryProperty, value);
    }

    public static readonly DependencyProperty ShelfDirectoryProperty =
        DependencyProperty.Register(nameof(ShelfDirectory), typeof(string),
            typeof(ProjectsConfigEditorControl), new PropertyMetadata(string.Empty, OnFieldChanged));

    public string ShelfDirectory
    {
        get => (string)GetValue(ShelfDirectoryProperty);
        set => SetValue(ShelfDirectoryProperty, value);
    }

    private static void OnFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ProjectsConfigEditorControl)d).MarkDirty();

    // ── IConfigChangeTracker / IConfigValidation ──

    public bool HasChanges => _hasChanges;
    public event EventHandler? HasChangesChanged;

    public bool IsValid { get; private set; } = true;
    public event EventHandler? IsValidChanged;

    private void MarkDirty()
    {
        if (_suppressDirty || _hasChanges) return;
        _hasChanges = true;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Load ──

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_config is not null) return;              // Loaded can fire again on re-show; build once.
        _config = DataContext as ProjectsConfig;
        if (_config is null) return;

        _suppressDirty = true;
        try
        {
            EnableProjects   = _config.EnableProjects;
            ProjectDirectory = _config.ProjectDirectory;
            ArchiveDirectory = _config.ArchiveDirectory;
            ShelfDirectory   = _config.ShelfDirectory;

            _statuses.CollectionChanged += OnStatusesChanged;
            foreach (var s in _config.BacklogStatuses) _statuses.Add(new StatusRow(s));
            StatusList.ItemsSource = _statuses;
            Revalidate();
        }
        finally { _suppressDirty = false; }
        _hasChanges = false;
    }

    // ── Folder browse ──

    private async void BrowseProject_Click(object sender, RoutedEventArgs e)
    {
        if (await PickFolder(ProjectDirectory) is { } p) ProjectDirectory = p;
    }

    private async void BrowseArchive_Click(object sender, RoutedEventArgs e)
    {
        if (await PickFolder(ArchiveDirectory) is { } p) ArchiveDirectory = p;
    }

    private async void BrowseShelf_Click(object sender, RoutedEventArgs e)
    {
        if (await PickFolder(ShelfDirectory) is { } p) ShelfDirectory = p;
    }

    private async System.Threading.Tasks.Task<string?> PickFolder(string? initial)
        => _shell is null ? null : await _shell.PickFolderAsync(string.IsNullOrWhiteSpace(initial) ? null : initial);

    // ── Status list lifecycle ──

    private void OnStatusesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (StatusRow r in e.OldItems) r.PropertyChanged -= OnStatusRowChanged;
        if (e.NewItems is not null)
            foreach (StatusRow r in e.NewItems) r.PropertyChanged += OnStatusRowChanged;
        Revalidate();
        MarkDirty();
    }

    private void OnStatusRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        if (e.PropertyName == nameof(StatusRow.IsTerminalCancelled) && sender is StatusRow row && row.IsTerminalCancelled)
        {
            // Radio behaviour: only one terminal state at a time.
            foreach (var other in _statuses)
                if (other != row && other.IsTerminalCancelled) other.IsTerminalCancelled = false;
        }
        if (e.PropertyName is nameof(StatusRow.Label) or nameof(StatusRow.IsTerminalCancelled))
            Revalidate();
    }

    private void AddStatus_Click(object sender, RoutedEventArgs e)
    {
        var row = new StatusRow(UniqueKey("status"))
        {
            Label     = UniqueLabel("New Status"),
            SwatchKey = "Swatch.Slate",
        };
        _statuses.Add(row);
    }

    private void RemoveStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StatusRow row }) _statuses.Remove(row);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StatusRow row })
        {
            var i = _statuses.IndexOf(row);
            if (i > 0) _statuses.Move(i, i - 1);
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StatusRow row })
        {
            var i = _statuses.IndexOf(row);
            if (i >= 0 && i < _statuses.Count - 1) _statuses.Move(i, i + 1);
        }
    }

    private string UniqueKey(string seed)
    {
        var baseKey = Slug(seed);
        if (_statuses.All(r => r.Key != baseKey) && (_config?.BacklogStatuses.All(s => s.Key != baseKey) ?? true))
            return baseKey;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseKey}-{i}";
            if (_statuses.All(r => r.Key != candidate)) return candidate;
        }
    }

    private string UniqueLabel(string seed)
    {
        if (_statuses.All(r => !string.Equals(r.Label, seed, StringComparison.OrdinalIgnoreCase))) return seed;
        for (var i = 2; ; i++)
        {
            var candidate = $"{seed} {i}";
            if (_statuses.All(r => !string.Equals(r.Label, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private static string Slug(string s)
    {
        var chars = s.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "status" : slug;
    }

    // ── Validation ──

    private void Revalidate()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _statuses)
        {
            var k = (r.Label ?? string.Empty).Trim();
            if (k.Length > 0) counts[k] = counts.GetValueOrDefault(k) + 1;
        }

        var anyError = false;
        foreach (var r in _statuses)
        {
            var k = (r.Label ?? string.Empty).Trim();
            var err = k.Length == 0 || counts.GetValueOrDefault(k) > 1;
            r.HasLabelError = err;
            anyError |= err;
        }

        var terminalCount = _statuses.Count(r => r.IsTerminalCancelled);
        var nowValid = !anyError && _statuses.Count > 0 && terminalCount == 1;

        if (nowValid != IsValid)
        {
            IsValid = nowValid;
            IsValidChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── ICustomConfigApply ──

    public void Apply()
    {
        if (_config is null) return;
        _config.EnableProjects   = EnableProjects;
        _config.ProjectDirectory = ProjectDirectory ?? string.Empty;
        _config.ArchiveDirectory = ArchiveDirectory ?? string.Empty;
        _config.ShelfDirectory   = ShelfDirectory ?? string.Empty;
        _config.BacklogStatuses  = _statuses.Select(r => r.ToModel()).ToList();

        _hasChanges = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── StatusRow — observable working copy of one BacklogStatusDef ──

    public sealed class StatusRow : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _swatchKey = "Swatch.Slate";
        private bool   _isTerminal;
        private bool   _hasLabelError;

        /// <summary>Stable key — never changes once created.</summary>
        public string Key { get; }

        public string Label     { get => _label;     set { if (_label == value) return; _label = value; PC(); } }
        public string SwatchKey { get => _swatchKey; set { if (_swatchKey == value) return; _swatchKey = value ?? "Swatch.Slate"; PC(); } }

        public bool IsTerminalCancelled { get => _isTerminal;    set { if (_isTerminal == value) return; _isTerminal = value; PC(); } }
        public bool HasLabelError       { get => _hasLabelError; set { if (_hasLabelError == value) return; _hasLabelError = value; PC(); } }

        public StatusRow(BacklogStatusDef d)
        {
            Key         = d.Key;
            _label      = d.Label;
            _swatchKey  = d.SwatchKey;
            _isTerminal = d.IsTerminalCancelled;
        }

        public StatusRow(string key) => Key = key;

        public BacklogStatusDef ToModel() => new()
        {
            Key                 = Key,
            Label               = (_label ?? string.Empty).Trim(),
            SwatchKey           = _swatchKey,
            IsTerminalCancelled = _isTerminal,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void PC([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
