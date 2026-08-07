using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.OneDrive.Services;

namespace Nexaflow.Features.OneDrive.Controls;

/// <summary>One editable line: a detected account, or a folder the user added.</summary>
internal sealed class SyncFolderRow : INotifyPropertyChanged
{
    private string _label = string.Empty;
    private bool   _shown = true;

    public required string Id         { get; init; }
    public required string FolderPath { get; init; }

    /// <summary>The name detection produced, so an edit back to it stops counting as an override.</summary>
    public required string DetectedLabel { get; init; }

    /// <summary>Found on this PC — its folder is fixed and it can't be removed, only hidden.</summary>
    public required bool IsDetected { get; init; }

    public bool IsCustom => !IsDetected;

    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(); } }
    }

    /// <summary>The inverse of the stored "hidden" flag — a tick meaning "show this" reads better in a
    /// list than a tick meaning "hide this".</summary>
    public bool Shown
    {
        get => _shown;
        set { if (_shown != value) { _shown = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// OneDrive's Options page. Detected sync folders and hand-added ones are shown in one list; the
/// detected ones can be renamed or hidden but not re-pointed, because their folder is OneDrive's to
/// choose.
/// </summary>
public partial class OneDriveOptionsControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    private readonly ObservableCollection<SyncFolderRow> _rows = [];
    private OneDriveConfig? _config;
    private bool _hasChanges;

    public OneDriveOptionsControl()
    {
        InitializeComponent();
        RowsList.ItemsSource = _rows;
        DataContextChanged += (_, _) => Load();
        Loaded += (_, _) => Load();
    }

    // ── IConfigChangeTracker ──────────────────────────────────────────────────

    public bool HasChanges
    {
        get => _hasChanges;
        private set
        {
            if (_hasChanges == value) return;
            _hasChanges = value;
            HasChangesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? HasChangesChanged;

    // ── Load ──────────────────────────────────────────────────────────────────

    private void Load()
    {
        if (DataContext is not OneDriveConfig config || ReferenceEquals(config, _config)) return;
        _config = config;

        foreach (var row in _rows) row.PropertyChanged -= OnRowEdited;
        _rows.Clear();

        IReadOnlyList<Models.SyncAccount> detected;
        try { detected = new OneDriveDetector(new HkcuRegistryView()).Detect(); }
        catch { detected = []; }

        foreach (var account in detected)
        {
            var over = config.Overrides.FirstOrDefault(
                o => string.Equals(o.Id, account.Id, StringComparison.OrdinalIgnoreCase));

            Add(new SyncFolderRow
            {
                Id            = account.Id,
                FolderPath    = account.FolderPath,
                DetectedLabel = account.Label,
                IsDetected    = true,
                Label         = string.IsNullOrWhiteSpace(over?.Label) ? account.Label : over!.Label!,
                Shown         = over is not { Hidden: true },
            });
        }

        foreach (var entry in config.Custom)
            Add(new SyncFolderRow
            {
                Id            = entry.Id,
                FolderPath    = entry.FolderPath,
                DetectedLabel = entry.Label,
                IsDetected    = false,
                Label         = entry.Label,
                Shown         = true,
            });

        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HasChanges = false;
    }

    private void Add(SyncFolderRow row)
    {
        row.PropertyChanged += OnRowEdited;
        _rows.Add(row);
    }

    private void OnRowEdited(object? sender, PropertyChangedEventArgs e) => HasChanges = true;

    // ── Commands ──────────────────────────────────────────────────────────────

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        // A [CustomControl] editor is handed only the config as its DataContext — no IShellServices to
        // route a picker through — so it opens the OS dialog directly, as ExternalAppsEditorControl does.
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to show under This PC" };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (_rows.Any(r => string.Equals(r.FolderPath.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'),
                                         StringComparison.OrdinalIgnoreCase)))
            return;   // already listed, detected or otherwise

        Add(new SyncFolderRow
        {
            Id            = "onedrive.custom." + Guid.NewGuid().ToString("N")[..8],
            FolderPath    = path,
            DetectedLabel = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')),
            IsDetected    = false,
            Label         = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')),
            Shown         = true,
        });

        EmptyHint.Visibility = Visibility.Collapsed;
        HasChanges = true;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SyncFolderRow row) return;
        row.PropertyChanged -= OnRowEdited;
        _rows.Remove(row);
        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HasChanges = true;
    }

    // ── ICustomConfigApply ────────────────────────────────────────────────────

    public void Apply()
    {
        if (_config is null) return;

        // Only record an override where the user actually departed from what was detected — storing a
        // no-op override for every account would make a later change to the detected label invisible.
        _config.Overrides =
        [
            .. _rows.Where(r => r.IsDetected)
                    .Where(r => !r.Shown || !string.Equals(r.Label, r.DetectedLabel, StringComparison.Ordinal))
                    .Select(r => new SyncFolderOverride(
                        r.Id,
                        string.Equals(r.Label, r.DetectedLabel, StringComparison.Ordinal) ? null : r.Label,
                        !r.Shown))
        ];

        _config.Custom =
        [
            .. _rows.Where(r => r.IsCustom)
                    .Select(r => new SyncFolderEntry(r.Id, r.Label, r.FolderPath))
        ];

        HasChanges = false;
        _config.RaiseChanged();   // an open This PC tab re-queries without a switch away and back
    }
}
