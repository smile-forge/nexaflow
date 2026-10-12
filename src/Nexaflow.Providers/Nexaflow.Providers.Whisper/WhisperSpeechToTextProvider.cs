using System.Runtime.Intrinsics.X86;
using Nexaflow.Providers.Common;
using Whisper.net;

namespace Nexaflow.Providers.Whisper;

/// <summary>
/// Speech to text on this machine, through whisper.cpp (Whisper.net): on the GPU when CUDA is there, otherwise on a
/// CPU with AVX. The model's file comes from the asset store, described by the catalog this assembly ships; it is
/// fetched when the model is first assigned, and the provider is available once it is on disk.
/// </summary>
public sealed class WhisperSpeechToTextProvider : ISpeechToTextProvider, IDisposable
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
        _model           = Catalog.FirstOrDefault(m => m.Id == model.Model);
    }

    /// <summary>The models this assembly can fetch, read once from its embedded manifest.</summary>
    internal static IReadOnlyList<CatalogModel> Catalog { get; } = ReadCatalog();

    private static IReadOnlyList<CatalogModel> ReadCatalog()
    {
        using var json = typeof(WhisperSpeechToTextProvider).Assembly.GetManifestResourceStream("whisper-models.json")
                         ?? throw new InvalidOperationException("The Whisper model catalog is missing from its assembly.");
        return ModelCatalog.Read(json);
    }

    public DataResidency Residency => DataResidency.StaysOnMachine;

    /// <summary>whisper.cpp's CPU build needs AVX; the model must be on disk and verified.</summary>
    public bool IsAvailable => Avx.IsSupported && ModelPath is not null;

    public event EventHandler? AvailabilityChanged;

    private string? ModelPath => _model is { Files: [var file, ..] } ? _assets.Locate(file) : null;

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. Catalog.Select(m => m.Id)]);

    /// <summary>Fetches the model when it is not on disk yet, reporting how far it has got.</summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        if (_model is not { Files: [var file, ..] } || ModelPath is not null) return;

        var activity = _activityManager.StartActivity($"Downloading {_model.DisplayName}…");
        var lastPercent = -1;
        var progress = new Progress<AssetProgress>(p =>
        {
            var percent = (int)(p.Fraction * 100);
            if (percent == lastPercent) return;
            lastPercent = percent;
            activity.Update($"Downloading {_model.DisplayName}… {percent}%");
        });
        try
        {
            await _assets.EnsureAsync(file, progress, ct);
            activity.Complete();
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);   // warm-up must not throw; the model stays unavailable until a later one
        }
    }

    public Task CooldownAsync(CancellationToken ct = default)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default)
    {
        var path = ModelPath ?? throw new LlmProviderException($"The Whisper model '{_model?.Id}' is not downloaded.");

        WhisperFactory factory;
        lock (_factoryLock) factory = _factory ??= WhisperFactory.FromPath(path);

        // An English-only model knows one language; otherwise say the one being spoken, or let it listen for it.
        var language = _model!.Id.EndsWith(".en", StringComparison.Ordinal) ? "en" : options.Language ?? "auto";
        return Task.FromResult<ISpeechToTextSession>(new WhisperSession(factory, language));
    }

    public void Dispose()
    {
        lock (_factoryLock)
        {
            _factory?.Dispose();
            _factory = null;
        }
    }
}
