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
        EnvGrid.ItemsSource = _rows;
    }

    public void Apply()
    {
        if (_config is null) return;
        _config.Environments = _rows.Select(r => r.ToModel()).ToList();
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

        if (row.IsDefault && _rows.Count > 1)
        {
            MessageBox.Show(
                "Assign another environment as the default before removing this one.",
                "Cannot Remove Default",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _rows.Remove(row);

        if (row.IsDefault && _rows.Count > 0)
            _rows[0].IsDefault = true;
    }

    private void IsDefault_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is EnvRow checkedRow)
            foreach (var r in _rows.Where(r => r != checkedRow))
                r.IsDefault = false;
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
