using Nexaflow.Features.Common;
using Nexaflow.Features.Console.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Console.Controls;

public partial class ConsoleEnvironmentsEditorControl : UserControl, ICustomConfigApply
{
    private ObservableCollection<EnvRow> _rows = [];
    private ConsoleConfig? _config;

    public ConsoleEnvironmentsEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _config = DataContext as ConsoleConfig;
        if (_config is null) return;
        _rows = new ObservableCollection<EnvRow>(_config.Environments.Select(env => new EnvRow(env)));

        // Invariant: always at least one environment, and exactly one flagged default.
        if (_rows.Count == 0)
            _rows.Add(new EnvRow(new ConsoleEnvironment
            {
                Name = "Default", TabTitle = "Console", IsDefault = true
            }));
        EnsureSingleDefault();

        EnvGrid.ItemsSource = _rows;
    }

    public void Apply()
    {
        if (_config is null) return;
        EnsureSingleDefault();
        _config.Environments = _rows.Select(r => r.ToModel()).ToList();
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
        var row = new EnvRow(new ConsoleEnvironment
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
        if (EnvGrid.SelectedItem is not EnvRow row) return;

        if (_rows.Count <= 1)
        {
            MessageBox.Show(
                "At least one environment is required.",
                "Cannot Remove",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _rows.Remove(row);

        // Removing the default promotes the new first row so one default always remains.
        if (row.IsDefault)
            _rows[0].IsDefault = true;
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
        private bool    _isDefault;

        public string  Name           { get => _name;           set { _name = value;           PC(); } }
        public string  TabTitle       { get => _tabTitle;       set { _tabTitle = value;       PC(); } }
        public string  LocationFilter { get => _locationFilter; set { _locationFilter = value; PC(); } }
        public string? InitialCommand { get => _initialCommand; set { _initialCommand = value; PC(); } }
        public bool    IsDefault      { get => _isDefault;      set { _isDefault = value;      PC(); } }

        public EnvRow(ConsoleEnvironment src)
        {
            _name           = src.Name;
            _tabTitle       = src.TabTitle;
            _locationFilter = src.LocationFilter;
            _initialCommand = src.InitialCommand;
            _isDefault      = src.IsDefault;
        }

        public ConsoleEnvironment ToModel() => new()
        {
            Name           = _name,
            TabTitle       = _tabTitle,
            LocationFilter = _locationFilter,
            InitialCommand = string.IsNullOrWhiteSpace(_initialCommand) ? null : _initialCommand,
            IsDefault      = _isDefault
        };

        private void PC([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
