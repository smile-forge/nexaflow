using System.Windows;
using System.Windows.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

public partial class VoiceConfigControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    private VoiceConfigViewModel? _viewModel;

    public VoiceConfigControl()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null && DataContext is VoiceConfig config)
        {
            _viewModel = new VoiceConfigViewModel(config);
            _viewModel.HasChangesChanged += (_, _) => HasChangesChanged?.Invoke(this, EventArgs.Empty);
            DataContext = _viewModel;
        }

        // Live-refresh the status as a download starts / advances / finishes.
        WhisperModelManager.Instance.ModelReadyChanged       += OnModelStatusChanged;
        WhisperModelManager.Instance.DownloadProgressChanged += OnModelStatusChanged;
        _viewModel?.RefreshStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        WhisperModelManager.Instance.ModelReadyChanged       -= OnModelStatusChanged;
        WhisperModelManager.Instance.DownloadProgressChanged -= OnModelStatusChanged;
    }

    private void OnModelStatusChanged(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => _viewModel?.RefreshStatus());

    public void Apply() => _viewModel?.Apply();

    // IConfigChangeTracker
    public bool HasChanges => _viewModel?.HasChanges ?? false;
    public event EventHandler? HasChangesChanged;
}
