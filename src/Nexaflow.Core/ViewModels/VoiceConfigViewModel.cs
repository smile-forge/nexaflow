using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// Backs <see cref="Controls.VoiceConfigControl"/>: edits the two Voice enums and shows whether the
/// selected Whisper model is present locally, downloading (with %), or not yet downloaded.
/// </summary>
public partial class VoiceConfigViewModel : ObservableObject
{
    private readonly VoiceConfig      _config;
    private readonly WhisperModelSize _origSize;
    private readonly WhisperLanguage  _origLang;

    public Array ModelSizes { get; } = Enum.GetValues<WhisperModelSize>();
    public Array Languages  { get; } = Enum.GetValues<WhisperLanguage>();

    [ObservableProperty] private WhisperModelSize _selectedSize;
    [ObservableProperty] private WhisperLanguage  _selectedLanguage;
    [ObservableProperty] private string _statusText      = string.Empty;
    [ObservableProperty] private bool   _isDownloading;
    [ObservableProperty] private int    _downloadPercent;
    [ObservableProperty] private bool   _canDownload;

    public bool HasChanges => SelectedSize != _origSize || SelectedLanguage != _origLang;
    public event EventHandler? HasChangesChanged;

    public VoiceConfigViewModel(VoiceConfig config)
    {
        _config       = config;
        _origSize     = config.ModelSize;
        _origLang     = config.Language;
        _selectedSize = config.ModelSize;
        _selectedLanguage = config.Language;
        RefreshStatus();
    }

    partial void OnSelectedSizeChanged(WhisperModelSize value)
    {
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
        RefreshStatus();
    }

    partial void OnSelectedLanguageChanged(WhisperLanguage value)
    {
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
        RefreshStatus();
    }

    /// <summary>Recomputes the status line for the currently selected model.</summary>
    public void RefreshStatus()
    {
        var probe = new VoiceConfig { ModelSize = SelectedSize, Language = SelectedLanguage };
        var mgr   = WhisperModelManager.Instance;

        if (mgr.IsModelReady(probe))
        {
            StatusText      = "✓  Model downloaded and ready.";
            IsDownloading   = false;
            CanDownload     = false;
            DownloadPercent = 100;
        }
        else if (mgr.IsDownloading)
        {
            IsDownloading   = true;
            DownloadPercent = mgr.DownloadPercent;
            StatusText      = $"Downloading…  {mgr.DownloadPercent}%";
            CanDownload     = false;
        }
        else
        {
            IsDownloading   = false;
            DownloadPercent = 0;
            CanDownload     = true;
            StatusText      = "Not downloaded yet — it will download automatically the first time you use voice input.";
        }
    }

    [RelayCommand]
    private void Download()
    {
        // Download the selected model without mutating the saved config (Apply does that on Save).
        WhisperModelManager.Instance.EnsureModelDownloaded(
            new VoiceConfig { ModelSize = SelectedSize, Language = SelectedLanguage });
        RefreshStatus();
    }

    /// <summary>Writes the selection back to the real config (called by the Options Save flow).</summary>
    public void Apply()
    {
        _config.ModelSize = SelectedSize;
        _config.Language  = SelectedLanguage;
    }
}
