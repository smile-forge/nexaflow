using System.Runtime.Intrinsics.X86;
using Nexaflow.Providers.Common;
using Whisper.net;

namespace Nexaflow.Providers.Whisper;

/// <summary>
/// Speech to text on this machine, through whisper.cpp (Whisper.net) on a CPU with AVX — what listens wherever
/// Nexaflow's own speech models are not there yet, or not chosen. The model's file comes from the asset store, described by the catalog this assembly ships; it is
/// fetched when the model is first assigned, and the provider is available once it is on disk.
/// </summary>
public sealed class WhisperSpeechToTextProvider : ISpeechToTextProvider, ILocalModelCatalog, IDisposable
{
    public const string ProviderName = "Whisper";
    public string Name => ProviderName;

    private readonly IBackgroundActivityManager _activityManager;
    private readonly IModelAssetStore           _assets;
    private readonly CatalogModel?              _model;
    private readonly Lock                       _factoryLock = new();
    private WhisperFactory?                     _factory;

    public WhisperSpeechToTextProvider(IBackgroundActivityManager activityManager, IModelAssetStore assets, ProviderModel model)
    {
        _activityManager = activityManager;
        _assets          = assets;
        _model           = Models.FirstOrDefault(m => m.Id == model.Model);
        _assets.Changed += OnAssetChanged;
    }

    /// <summary>The models this assembly can fetch, read once from its embedded manifest.</summary>
    internal static IReadOnlyList<CatalogModel> Models { get; } = ReadCatalog();

    public IReadOnlyList<CatalogModel> Catalog => Models;

    /// <summary>This model's file arrived or went — downloaded here, from the workspace's Whisper page, or removed there.</summary>
    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is { Files: [var file, ..] } && file == asset) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<CatalogModel> ReadCatalog()
    {
        using var json = typeof(WhisperSpeechToTextProvider).Assembly.GetManifestResourceStream("whisper-models.json")
                         ?? throw new InvalidOperationException("The Whisper model catalog is missing from its assembly.");
        return ModelCatalog.Read(json);
    }

    public DataResidency Residency => DataResidency.StaysOnMachine;

    /// <summary>whisper.cpp runs in this process: Nexaflow is the whole harness.</summary>
    public ModelHarness Harness => ModelHarness.Owned;

    /// <summary>whisper.cpp's CPU build needs AVX; the model must be on disk and verified.</summary>
    public bool IsAvailable => Avx.IsSupported && ModelPath is not null;

    public event EventHandler? AvailabilityChanged;

    private string? ModelPath => _model is { Files: [var file, ..] } ? _assets.Locate(file) : null;

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. Models.Select(m => m.Id)]);

    /// <summary>
    /// Fetches the model when it is not on disk yet, reporting how far it has got, then loads it — the weights take a
    /// second or more, which the first press of the mic should not wait for.
    /// </summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        if (_model is not { Files: [var file, ..] }) return;
        if (ModelPath is null && !await DownloadAsync(_model, file, ct)) return;

        try
        {
            if (ModelPath is { } path) await Task.Run(() => Factory(path), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Warm-up must not throw; a model that will not load says why when a session asks for it.
        }
    }

    private async Task<bool> DownloadAsync(CatalogModel model, ModelAsset file, CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Downloading {model.DisplayName}…");
        var lastPercent = -1;
        var progress = new Progress<AssetProgress>(p =>
        {
            var percent = (int)(p.Fraction * 100);
            if (percent == lastPercent) return;
            lastPercent = percent;
            activity.Update($"Downloading {model.DisplayName}… {percent}%");
        });
        try
        {
            await _assets.EnsureAsync(file, progress, ct);
            activity.Complete();   // the store's Changed event says the model is here
            return true;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);   // the model stays unavailable until a later warm-up fetches it
            return false;
        }
    }

    /// <summary>The loaded model, loaded once and shared by every session.</summary>
    private WhisperFactory Factory(string path)
    {
        lock (_factoryLock) return _factory ??= WhisperFactory.FromPath(path);
    }

    public Task CooldownAsync(CancellationToken ct = default)
    {
        lock (_factoryLock)
        {
            _factory?.Dispose();
            _factory = null;
        }
        return Task.CompletedTask;
    }

    public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default)
    {
        var path = ModelPath ?? throw new LlmProviderException($"The Whisper model '{_model?.Id}' is not downloaded.");

        var factory = Factory(path);

        // An English-only model knows one language; otherwise say the one being spoken, or let it listen for it.
        var language = _model!.Id.EndsWith(".en", StringComparison.Ordinal) ? "en" : options.Language ?? "auto";
        return Task.FromResult<ISpeechToTextSession>(new WhisperSession(factory, language));
    }

    public void Dispose()
    {
        _assets.Changed -= OnAssetChanged;
        lock (_factoryLock)
        {
            _factory?.Dispose();
            _factory = null;
        }
    }
}
