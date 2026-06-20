using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Console.Controls;

public partial class ConsoleEnvironmentsEditorControl
    : UserControl, ICustomConfigApply, IConfigChangeTracker, IShellAware
{
    private ObservableCollection<EnvRow> _rows = [];
    private ConsoleConfig? _config;
    private bool _hasChanges;
    private IShellServices? _shell;

    public ConsoleEnvironmentsEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // The Configure host injects the shell so the browse buttons use the themed file/folder pickers.
    public void AttachShell(IShellServices shell) => _shell = shell;

    // IConfigChangeTracker — lets the Configure panel disable Apply until the user edits something.
    public bool HasChanges => _hasChanges;
    public event EventHandler? HasChangesChanged;

    private void MarkDirty()
    {
        if (_hasChanges) return;
        _hasChanges = true;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can fire again after the control is re-shown; only build once.
        if (_config is not null) return;
        _config = DataContext as ConsoleConfig;
        if (_config is null) return;
        _rows = new ObservableCollection<EnvRow>(_config.Environments.Select(env => new EnvRow(env)));

        // Invariant: always at least one environment, and exactly one flagged default.
        if (_rows.Count == 0)
            _rows.Add(new EnvRow(new TerminalEnvironment
            {
                Name = "Default", TabTitle = "Console", IsDefault = true
            }));
        EnsureSingleDefault();

        EnvGrid.ItemsSource = _rows;

        // Track edits only AFTER the initial (load-time) state is established.
        _rows.CollectionChanged += OnRowsChanged;
        foreach (var row in _rows) row.PropertyChanged += OnRowChanged;
        UpdateRemoveEnabled();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (EnvRow r in e.OldItems) r.PropertyChanged -= OnRowChanged;
        if (e.NewItems is not null)
            foreach (EnvRow r in e.NewItems) r.PropertyChanged += OnRowChanged;
        UpdateRemoveEnabled();
        MarkDirty();
    }

    // At least one environment must remain — disable Remove rather than popping a dialog.
    private void UpdateRemoveEnabled() => RemoveButton.IsEnabled = _rows.Count > 1;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    public void Apply()
    {
        if (_config is null) return;
        EnsureSingleDefault();
        _config.Environments = _rows.Select(r => r.ToModel()).ToList();

        // Saved — back to a clean state so Apply disables until the next edit.
        _hasChanges = false;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Guarantees exactly one row is the default: keeps the first flagged one,
    /// or promotes the first row when none is flagged.</summary>
    private void EnsureSingleDefault()
    {
        if (_rows.Count == 0) return;
        var defaults = _rows.Where(r => r.IsDefault).ToList();
        if (defaults.Count == 0)
        {
            _rows[0].IsDefault = true;
        }
        else if (defaults.Count > 1)
        {
            foreach (var extra in defaults.Skip(1))
                extra.IsDefault = false;
        }
    }

    private void AddEnv_Click(object sender, RoutedEventArgs e)
    {
        var row = new EnvRow(new TerminalEnvironment
        {
            Name      = "New Environment",
            TabTitle  = "Console",
            IsDefault = _rows.Count == 0
        });
        _rows.Add(row);
        EnvGrid.SelectedItem = row;
        EnvGrid.ScrollIntoView(row);
    }

    private void RemoveEnv_Click(object sender, RoutedEventArgs e)
    {
        if (EnvGrid.SelectedItem is not EnvRow row || _rows.Count <= 1) return;

        _rows.Remove(row);

        // Removing the default promotes the new first row so one default always remains.
        if (row.IsDefault)
            _rows[0].IsDefault = true;
    }

    // ── Themed browse buttons (DataContext of each button is its EnvRow) ──

    private async void LocationBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is null || (sender as FrameworkElement)?.DataContext is not EnvRow row) return;
        var seed   = row.LocationFilter is { Length: > 0 } lf && !lf.Contains('*') ? lf : null;
        var folder = await _shell.PickFolderAsync(seed);
        if (folder is not null) row.LocationFilter = folder.TrimEnd('\\') + "\\*";
    }

    private async void StartDirBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is null || (sender as FrameworkElement)?.DataContext is not EnvRow row) return;
        var folder = await _shell.PickFolderAsync(row.StartDirectory);
        if (folder is not null) row.StartDirectory = folder;
    }

    private async void InitialCmdBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is null || (sender as FrameworkElement)?.DataContext is not EnvRow row) return;
        var file = await _shell.PickOpenFileAsync();
        if (file is not null) row.InitialCommand = file.Contains(' ') ? $"\"{file}\"" : file;
    }

    private void IsDefault_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is EnvRow checkedRow)
            foreach (var r in _rows.Where(r => r != checkedRow))
                r.IsDefault = false;
    }

    private void IsDefault_Unchecked(object sender, RoutedEventArgs e)
    {
        // A default can't be cleared directly — there must always be one. Re-check it
        // unless another row already took over as default.
        if (sender is CheckBox { DataContext: EnvRow row }
            && !_rows.Any(r => r.IsDefault))
            row.IsDefault = true;
    }

    // ── EnvRow — observable wrapper for in-editor editing ─────────────────

    private sealed class EnvRow : INotifyPropertyChanged
    {
        private string  _name           = string.Empty;
        private string  _tabTitle       = string.Empty;
        private string  _locationFilter = "*";
        private string? _initialCommand;
        private string? _startDirectory;
        private Dictionary<string, string> _envOverrides;
        private bool    _isDefault;

        public string  Name           { get => _name;           set { _name = value;           PC(); } }
        public string  TabTitle       { get => _tabTitle;       set { _tabTitle = value;       PC(); } }
        public string  LocationFilter { get => _locationFilter; set { _locationFilter = value; PC(); } }
        public string? InitialCommand { get => _initialCommand; set { _initialCommand = value; PC(); } }
        public string? StartDirectory { get => _startDirectory; set { _startDirectory = value; PC(); } }
        public bool    IsDefault      { get => _isDefault;      set { _isDefault = value;      PC(); } }

        /// <summary>Env-var overrides as editable <c>KEY=VALUE</c> entries separated by ';' or newlines.</summary>
        public string OverridesText
        {
            get => string.Join("; ", _envOverrides.Select(kv => $"{kv.Key}={kv.Value}"));
            set { _envOverrides = ParseOverrides(value); PC(); }
        }

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
            _locationFilter = src.LocationFilter;
            _initialCommand = src.InitialCommand;
            _isDefault      = src.IsDefault;
            _startDirectory = src.StartDirectory;
            _envOverrides   = new Dictionary<string, string>(src.EnvOverrides, StringComparer.OrdinalIgnoreCase);
        }

        public TerminalEnvironment ToModel() => new()
        {
            Name           = _name,
            TabTitle       = _tabTitle,
            LocationFilter = _locationFilter,
            InitialCommand = string.IsNullOrWhiteSpace(_initialCommand) ? null : _initialCommand,
            IsDefault      = _isDefault,
            StartDirectory = string.IsNullOrWhiteSpace(_startDirectory) ? null : _startDirectory,
            EnvOverrides   = _envOverrides
        };

        private void PC([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
