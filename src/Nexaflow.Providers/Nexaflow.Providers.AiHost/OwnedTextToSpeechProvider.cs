using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Text to speech Nexaflow runs itself, in its AI host: Kokoro, with a voice for each language the model's catalog
/// entry names. Nothing leaves this machine.
/// </summary>
public sealed class OwnedTextToSpeechProvider : ITextToSpeechProvider, ILocalModelCatalog, IAsyncDisposable
{
    private readonly OwnedModelRuntime _runtime;
    private readonly OwnedModel?       _model;

    public OwnedTextToSpeechProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host,
                                     ComputeHardwareReport hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, hardware);
        _model   = OwnedModels.OfKind(ModelKind.TextToSpeech).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
    }

    public string Name => OwnedLlmProvider.ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;

    public IReadOnlyList<CatalogModel> Catalog => [.. OwnedModels.OfKind(ModelKind.TextToSpeech).Select(_runtime.CatalogOf)];

    public bool IsAvailable => _model is not null && _runtime.IsHere(_model);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Speaks(string language) => _model?.Voices.Any(v => string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase)) ?? false;

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. OwnedModels.OfKind(ModelKind.TextToSpeech).Select(m => m.Id)]);

    public Task WarmupAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.WarmupAsync(_model, ct);

    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    /// <summary>Opens a stretch of speaking in the host, at the voice priority: a reply being spoken is what someone is
    /// waiting on.</summary>
    public async Task<ITextToSpeechSession> StartSessionAsync(TextToSpeechOptions options, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{Name} has no voice model of that name.");
        var voices = _model.Voices.Select(v =>
        {
            var part = _model.PartNamed(v.Part, _runtime.ExecutionProvider);
            return new VoiceSpec(v.Language, _runtime.Spec(_model, part), v.File, v.Lexicon is { } lexicon ? _runtime.Locate(_model, part, lexicon) : null);
        }).ToList();

        var host = _runtime.Host;
        host.Environment = _runtime.Environment;
        try
        {
            var stream = await host.OpenStreamAsync(id => new Speak(id, voices, options.Speed, RequestPriority.Voice), OwnedModelRuntime.StallLimit, ct);
            return new OwnedSpeakingSession(stream);
        }
        catch (AiHostException ex)
        {
            throw new LlmProviderException($"{Name} could not start speaking: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _runtime.Assets.Changed -= OnAssetChanged;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A stretch of speaking in the host: segments streamed in, their audio streamed back.</summary>
internal sealed class OwnedSpeakingSession(IHostStream stream) : ITextToSpeechSession
{
    private int _segments;

    public async ValueTask<int> SpeakAsync(string text, string? language = null, CancellationToken ct = default)
    {
        int segment = _segments++;
        try { await stream.WriteAsync(new TextToSpeak(stream.RequestId, segment, text, language), ct); }
        catch (AiHostException ex) { throw new LlmProviderException($"Speaking stopped: {ex.Message}", ex); }
        return segment;
    }

    public async ValueTask CompleteAsync(CancellationToken ct = default)
    {
        try { await stream.WriteAsync(new InputEnd(stream.RequestId), ct); }
        catch (AiHostException ex) { throw new LlmProviderException($"Speaking stopped: {ex.Message}", ex); }
    }

    public async IAsyncEnumerable<TextToSpeechEvent> ReadAudioAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var replies = stream.ReadAsync(ct).GetAsyncEnumerator(ct);
        while (true)
        {
            try
            {
                if (!await replies.MoveNextAsync()) yield break;
            }
            catch (AiHostException ex)
            {
                throw new LlmProviderException($"Speaking stopped: {ex.Message}", ex);
            }
            switch (replies.Current)
            {
                case SpokenAudio audio:    yield return new SpeechAudioChunk(audio.Segment, audio.Pcm); break;
                case SpokenSegmentEnd end: yield return new SpeechSegmentEnd(end.Segment); break;
            }
        }
    }

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
