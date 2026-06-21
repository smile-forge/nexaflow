using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Console.Controls;

/// <summary>
/// Master/detail editor for the console's environments, plus the per-location defaults that the launch
/// picker remembers. The left list selects an environment; the right pane edits it. The collapsible
/// "Default environment by location" section surfaces the <see cref="ConsoleConfig.FolderBindings"/> so
/// the user can see and prune the "always use this environment here" memories set from the picker.
/// </summary>
public partial class ConsoleEnvironmentsEditorControl
    : UserControl, ICustomConfigApply, IConfigChangeTracker, IConfigValidation, IShellAware
{
    private readonly ObservableCollection<EnvRow>     _envs     = [];
    private readonly ObservableCollection<BindingRow> _bindings = [];

    // Live environment names offered to each location-default's environment dropdown.
    public ObservableCollection<string> EnvNames { get; } = [];

    // Last-known name per row, so a rename can carry its pinned locations along with it.
    private readonly Dictionary<EnvRow, string> _lastNames = [];

    private ConsoleConfig?  _config;
    private IShellServices? _shell;
    private bool _hasChanges;
    private bool _suppressDirty;

    public ConsoleEnvironmentsEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // The Configure host injects the shell so the browse button uses the themed file picker.
    public void AttachShell(IShellServices shell) => _shell = shell;

    // ── Dependency properties (detail binding + section state) ────────────────

    public static readonly DependencyProperty SelectedEnvProperty =
        DependencyProperty.Register(nameof(SelectedEnv), typeof(EnvRow),
            typeof(ConsoleEnvironmentsEditorControl), new PropertyMetadata(null, OnSelectedEnvChanged));

    public EnvRow? SelectedEnv
    {
        get => (EnvRow?)GetValue(SelectedEnvProperty);
        set => SetValue(SelectedEnvProperty, value);
    }

    public static readonly DependencyProperty HasSelectedEnvProperty =
        DependencyProperty.Register(nameof(HasSelectedEnv), typeof(bool),
            typeof(ConsoleEnvironmentsEditorControl), new PropertyMetadata(false));

    public bool HasSelectedEnv
    {
        get => (bool)GetValue(HasSelectedEnvProperty);
        private set => SetValue(HasSelectedEnvProperty, value);
    }

    public static readonly DependencyProperty LocationsExpandedProperty =
        DependencyProperty.Register(nameof(LocationsExpanded), typeof(bool),
            typeof(ConsoleEnvironmentsEditorControl), new PropertyMetadata(false));

    public bool LocationsExpanded
    {
        get => (bool)GetValue(LocationsExpandedProperty);
        set => SetValue(LocationsExpandedProperty, value);
    }

    private static void OnSelectedEnvChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ConsoleEnvironmentsEditorControl c) c.HasSelectedEnv = e.NewValue is not null;
    }

    // ── IConfigChangeTracker / IConfigValidation ─────────────────────────────

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

    // ── Load ─────────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can fire again after the control is re-shown; only build once.
        if (_config is not null) return;
        _config = DataContext as ConsoleConfig;
        if (_config is null) return;

        _suppressDirty = true;
        try
        {
            _envs.CollectionChanged += OnEnvsChanged;
            foreach (var env in _config.Environments) _envs.Add(new EnvRow(env));

            // Invariant: at least one environment so the picker is meaningful.
            if (_envs.Count == 0)
                _envs.Add(new EnvRow(new TerminalEnvironment { Name = "Default", TabTitle = "Console" }));
            EnvList.ItemsSource = _envs;

            _bindings.CollectionChanged += OnBindingsChanged;
            foreach (var b in _config.FolderBindings)
                _bindings.Add(new BindingRow { FolderPath = b.FolderPath, EnvName = b.EnvName });
            BindingsList.ItemsSource = _bindings;

            RebuildEnvNames();
            RecomputeStale();
            RevalidateNames();
        }
        finally { _suppressDirty = false; }

        UpdateRemoveEnabled();
        UpdateLocationsUi();
        if (_envs.Count > 0) EnvList.SelectedIndex = 0;   // open the first environment in the detail pane
        _hasChanges = false;
    }

    // ── Environment list lifecycle ────────────────────────────────────────────

    private void OnEnvsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (EnvRow r in e.OldItems)
            {
                r.PropertyChanged -= OnEnvRowChanged;
                _lastNames.Remove(r);
                RemoveBindingsForEnv(r.Name);   // a deleted environment can't stay pinned to a location
            }
        if (e.NewItems is not null)
            foreach (EnvRow r in e.NewItems)
            {
                _lastNames[r] = r.Name;
                r.PropertyChanged += OnEnvRowChanged;
            }

        RebuildEnvNames();
        RecomputeStale();
        RevalidateNames();
        UpdateRemoveEnabled();
        MarkDirty();
    }

    private void OnEnvRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        if (e.PropertyName == nameof(EnvRow.Name))
        {
            RebuildEnvNames();
            MigrateRenamedBinding(sender as EnvRow);
            RecomputeStale();
            RevalidateNames();
        }
    }

    // At least one environment must remain — disable Remove rather than popping a dialog.
    private void UpdateRemoveEnabled() => RemoveButton.IsEnabled = _envs.Count > 1;

    private void EnvList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => SelectedEnv = EnvList.SelectedItem as EnvRow;

    private void AddEnv_Click(object sender, RoutedEventArgs e)
    {
        var row = new EnvRow(new TerminalEnvironment
        {
            Name     = UniqueName("New Environment"),
            TabTitle = "Console",
        });
        _envs.Add(row);
        EnvList.SelectedItem = row;
        EnvList.ScrollIntoView(row);
    }

    private void RemoveEnv_Click(object sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvRow row || _envs.Count <= 1) return;

        _envs.Remove(row);                      // OnEnvsChanged prunes the row's pinned locations
        if (SelectedEnv == row || SelectedEnv is null) EnvList.SelectedIndex = 0;
    }

    private string UniqueName(string seed)
    {
        if (!_envs.Any(r => string.Equals(r.Name, seed, StringComparison.OrdinalIgnoreCase))) return seed;
        for (int i = 2; ; i++)
        {
            var candidate = $"{seed} {i}";
            if (!_envs.Any(r => string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    // ── Themed browse button (acts on the selected environment) ───────────────

    private async void InitialCmdBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is null || SelectedEnv is null) return;
        var file = await _shell.PickOpenFileAsync([".exe", ".bat", ".cmd", ".com"]);
        if (file is not null) SelectedEnv.InitialCommand = file.Contains(' ') ? $"\"{file}\"" : file;
    }

    // ── Per-location defaults ─────────────────────────────────────────────────

    private void OnBindingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (BindingRow b in e.OldItems) b.PropertyChanged -= OnBindingChanged;
        if (e.NewItems is not null) foreach (BindingRow b in e.NewItems) b.PropertyChanged += OnBindingChanged;
        RecomputeStale();
        UpdateLocationsUi();
        MarkDirty();
    }

    private void OnBindingChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        if (e.PropertyName == nameof(BindingRow.EnvName)) RecomputeStale();
    }

    private void BindingEnv_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The dropdown is one-way bound, so list rebuilds never null a pin; only a real user pick writes back.
        if (sender is ComboBox { SelectedItem: string name, DataContext: BindingRow row }
            && !string.Equals(row.EnvName, name, StringComparison.OrdinalIgnoreCase))
            row.EnvName = name;
    }

    private void RemoveBinding_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BindingRow row }) _bindings.Remove(row);
    }

    private void RemoveBindingsForEnv(string envName)
    {
        if (string.IsNullOrWhiteSpace(envName)) return;
        foreach (var b in _bindings.Where(b => string.Equals(b.EnvName, envName, StringComparison.OrdinalIgnoreCase)).ToList())
            _bindings.Remove(b);
    }

    /// <summary>Carries a renamed environment's pinned locations across to the new name,
    /// unless another environment still owns the old name.</summary>
    private void MigrateRenamedBinding(EnvRow? row)
    {
        if (row is null) return;
        var @new = row.Name;
        var old  = _lastNames.TryGetValue(row, out var o) ? o : @new;
        _lastNames[row] = @new;
        if (string.IsNullOrWhiteSpace(old) || string.Equals(old, @new, StringComparison.OrdinalIgnoreCase)) return;
        if (_envs.Any(r => r != row && string.Equals(r.Name, old, StringComparison.OrdinalIgnoreCase))) return;
        foreach (var b in _bindings.Where(b => string.Equals(b.EnvName, old, StringComparison.OrdinalIgnoreCase)).ToList())
            b.EnvName = @new;
    }

    private void UpdateLocationsUi()
    {
        LocationsCount.Text       = _bindings.Count > 0 ? $"({_bindings.Count})" : string.Empty;
        NoBindingsText.Visibility = _bindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BindingsBorder.Visibility = _bindings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Derived state ─────────────────────────────────────────────────────────

    private void RebuildEnvNames()
    {
        // Reconcile in place so dropdowns keep their selection when an unrelated name changes.
        var desired = _envs.Select(r => r.Name)
                           .Where(n => !string.IsNullOrWhiteSpace(n))
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToList();
        for (int i = EnvNames.Count - 1; i >= 0; i--)
            if (!desired.Contains(EnvNames[i], StringComparer.OrdinalIgnoreCase))
                EnvNames.RemoveAt(i);
        foreach (var d in desired)
            if (!EnvNames.Contains(d, StringComparer.OrdinalIgnoreCase))
                EnvNames.Add(d);
    }

    private void RecomputeStale()
    {
        foreach (var b in _bindings)
            b.IsStale = !string.IsNullOrWhiteSpace(b.EnvName)
                     && !_envs.Any(r => string.Equals(r.Name, b.EnvName, StringComparison.OrdinalIgnoreCase));
    }

    private void RevalidateNames()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _envs)
        {
            var k = (r.Name ?? string.Empty).Trim();
            if (k.Length > 0) counts[k] = counts.GetValueOrDefault(k) + 1;
        }

        bool anyError = false;
        foreach (var r in _envs)
        {
            var k = (r.Name ?? string.Empty).Trim();
            bool err = k.Length == 0 || counts.GetValueOrDefault(k) > 1;
            r.HasNameError = err;
            anyError |= err;
        }

        bool nowValid = !anyError;
        if (nowValid != IsValid)
        {
            IsValid = nowValid;
            IsValidChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── ICustomConfigApply ────────────────────────────────────────────────────

    public void Apply()
    {
        if (_config is null) return;
        _config.Environments = _envs.Select(r => r.ToModel()).ToList();
        _config.FolderBindings = _bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.FolderPath) && !string.IsNullOrWhiteSpace(b.EnvName))
            .Select(b => new TerminalEnvBinding { FolderPath = b.FolderPath, EnvName = b.EnvName })
            .ToList();

        _hasChanges = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── EnvRow — observable wrapper for the detail editor ─────────────────────

    public sealed class EnvRow : INotifyPropertyChanged
    {
        private string  _name     = string.Empty;
        private string  _tabTitle = string.Empty;
        private string? _initialCommand;
        private Dictionary<string, string> _envOverrides;
        private bool    _hasNameError;

        public string  Name           { get => _name;     set { if (_name == value) return; _name = value; PC(); PC(nameof(Summary)); } }
        public string  TabTitle       { get => _tabTitle; set { _tabTitle = value; PC(); } }
        public string? InitialCommand { get => _initialCommand; set { _initialCommand = value; PC(); PC(nameof(Summary)); } }
        public bool    HasNameError   { get => _hasNameError;   set { if (_hasNameError == value) return; _hasNameError = value; PC(); } }

        /// <summary>Env-var overrides as editable <c>NAME=value</c> lines (also tolerates ';' separators).</summary>
        public string OverridesText
        {
            get => string.Join(Environment.NewLine, _envOverrides.Select(kv => $"{kv.Key}={kv.Value}"));
            set { _envOverrides = ParseOverrides(value); PC(); }
        }

        /// <summary>The list-row subtitle: the initial command, or a note when there is none.</summary>
        public string Summary => string.IsNullOrWhiteSpace(_initialCommand) ? "no startup command" : _initialCommand!.Trim();

        private static Dictionary<string, string> ParseOverrides(string? text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (text ?? string.Empty).Split([';', '\n', '\r'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                d[part[..eq].Trim()] = part[(eq + 1)..].Trim();
            }
            return d;
        }

        public EnvRow(TerminalEnvironment src)
        {
            _name           = src.Name;
            _tabTitle       = src.TabTitle;
            _initialCommand = src.InitialCommand;
            _envOverrides   = new Dictionary<string, string>(src.EnvOverrides, StringComparer.OrdinalIgnoreCase);
        }

        public TerminalEnvironment ToModel() => new()
        {
            Name           = _name,
            TabTitle       = _tabTitle,
            InitialCommand = string.IsNullOrWhiteSpace(_initialCommand) ? null : _initialCommand,
            EnvOverrides   = _envOverrides,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void PC([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    // ── BindingRow — one "always use here" location default ───────────────────

    public sealed class BindingRow : INotifyPropertyChanged
    {
        private string _envName = string.Empty;
        private bool   _isStale;

        public string FolderPath { get; init; } = string.Empty;

        public string EnvName { get => _envName; set { var v = value ?? string.Empty; if (_envName == v) return; _envName = v; PC(); } }

        /// <summary>True when the pinned environment no longer exists (the row rings red).</summary>
        public bool IsStale { get => _isStale; set { if (_isStale == value) return; _isStale = value; PC(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void PC([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
