using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// A single selectable Whisper model: a (size, language) pair with a friendly label and a
/// one-line note on what the size is suited for. Combines the former Size/Language selectors
/// into one list so an impossible combo (large-v3 has no English-only build) can't be chosen.
/// </summary>
public sealed record VoiceModelOption(
    WhisperModelSize Size,
    WhisperLanguage  Language,
    string           DisplayName,
    string           Description);

/// <summary>
/// Backs <see cref="Controls.VoiceConfigControl"/>: picks a single Whisper model from a combined
/// list and shows whether it is present locally, downloading (with %), or not yet downloaded.
/// </summary>
public partial class VoiceConfigViewModel : ObservableObject
{
    private readonly VoiceConfig _config;
    private readonly VoiceModelOption _original;

    public IReadOnlyList<VoiceModelOption> Models { get; } = BuildModels();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDescription))]
    private VoiceModelOption _selectedModel;

    [ObservableProperty] private string _statusText      = string.Empty;
    [ObservableProperty] private bool   _isDownloading;
    [ObservableProperty] private int    _downloadPercent;
    [ObservableProperty] private bool   _canDownload;

    public string SelectedDescription => SelectedModel.Description;

    public bool HasChanges =>
        SelectedModel.Size != _original.Size || SelectedModel.Language != _original.Language;

    public event EventHandler? HasChangesChanged;

    public VoiceConfigViewModel(VoiceConfig config)
    {
        _config   = config;
        _original = Match(config.ModelSize, config.Language);
        _selectedModel = _original;
        RefreshStatus();
    }

    private VoiceModelOption Match(WhisperModelSize size, WhisperLanguage language) =>
        Models.FirstOrDefault(m => m.Size == size && m.Language == language)
        ?? Models.First(m => m.Size == size)   // language unavailable for this size → first variant
        ?? Models[0];

    partial void OnSelectedModelChanged(VoiceModelOption value)
    {
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
        RefreshStatus();
    }

    /// <summary>Recomputes the status line for the currently selected model.</summary>
    public void RefreshStatus()
    {
        var probe = new VoiceConfig { ModelSize = SelectedModel.Size, Language = SelectedModel.Language };
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
            new VoiceConfig { ModelSize = SelectedModel.Size, Language = SelectedModel.Language });
        RefreshStatus();
    }

    /// <summary>Writes the selection back to the real config (called by the Options Save flow).</summary>
    public void Apply()
    {
        _config.ModelSize = SelectedModel.Size;
        _config.Language  = SelectedModel.Language;
    }

    // ── Catalogue of selectable models ────────────────────────────────────────

    private static List<VoiceModelOption> BuildModels()
    {
        const string baseDesc   = "Fastest and smallest (~150 MB). Good for quick notes and clear speech; lowest accuracy.";
        const string smallDesc  = "Balanced speed and accuracy (~500 MB). A solid default for everyday dictation.";
        const string mediumDesc = "High accuracy, slower (~1.5 GB). Handles technical terms, names and accents well.";
        const string largeDesc  = "Best accuracy, slowest (~3 GB). Multilingual only — use when accuracy matters most.";

        return
        [
            new(WhisperModelSize.Base,    WhisperLanguage.EnglishOnly,  "Base · English-only",    baseDesc),
            new(WhisperModelSize.Base,    WhisperLanguage.Multilingual, "Base · Multilingual",    baseDesc),
            new(WhisperModelSize.Small,   WhisperLanguage.EnglishOnly,  "Small · English-only",   smallDesc),
            new(WhisperModelSize.Small,   WhisperLanguage.Multilingual, "Small · Multilingual",   smallDesc),
            new(WhisperModelSize.Medium,  WhisperLanguage.EnglishOnly,  "Medium · English-only",  mediumDesc),
            new(WhisperModelSize.Medium,  WhisperLanguage.Multilingual, "Medium · Multilingual",  mediumDesc),
            // No large-v3.en build exists — multilingual only.
            new(WhisperModelSize.LargeV3, WhisperLanguage.Multilingual, "Large v3 · Multilingual", largeDesc),
        ];
    }
}
