using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Text to speech Nexaflow runs itself, in its AI host: Kokoro, with a voice for each language the model's catalog
/// entry names. Nothing leaves this machine.
/// </summary>
public sealed class OwnedTextToSpeechProvider : ITextToSpeechProvider, ILocalModelCatalog, IHostModelCatalog, ITracksModelUse, IAsyncDisposable
{
    private readonly OwnedModelRuntime _runtime;
    private readonly HostedModel?       _model;

    public OwnedTextToSpeechProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostHolds holds,
                                     MachineCards hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, holds, hardware);
        _model   = OwnedModels.OfKind(ModelKind.TextToSpeech).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
    }

    public string Name => OwnedLlmProvider.ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;

    public IReadOnlyList<CatalogModel> Catalog => [.. _runtime.Runnable(ModelKind.TextToSpeech).Select(_runtime.CatalogOf)];

    public IReadOnlyList<UnrunnableModel> Unrunnable => [.. _runtime.Unrunnable(ModelKind.TextToSpeech)];

    public LocalRuntime? RuntimeOf(string modelId) => _runtime.Runtime;

    public bool LoadsOnDemand => false;

    public IReadOnlyList<PlacementNeed> NeedsOf(string modelId)
        => OwnedModels.OfKind(ModelKind.TextToSpeech).FirstOrDefault(m => m.Id == modelId) is { } model ? _runtime.NeedsOf(model) : [];

    public bool IsAvailable => _model is not null && _runtime.IsHere(_model);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Speaks(string language) => _model?.Voices.Any(v => string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase)) ?? false;

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. _runtime.Runnable(ModelKind.TextToSpeech).Select(m => m.Id)]);

    public Task WarmupAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.WarmupAsync(_model, ct);

    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    public ModelUseTracker ModelUse => _runtime.ModelUse;

    /// <summary>Opens a stretch of speaking in the host, at the voice priority: a reply being spoken is what someone is
    /// waiting on.</summary>
    public async Task<ITextToSpeechSession> StartSessionAsync(TextToSpeechOptions options, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{Name} has no voice model of that name.");
        var use = await _runtime.ModelUse.BeginAsync(ct);   // the session's whole stretch is one use, ended with it
        HostHold? voice = null;
        try
        {
            // The voices held with the model are held at the voice priority for the session's length; one loaded on demand the host loads
            // with the first sentence in its language.
            foreach (var part in _model.Voices.Select(v => _model.PartNamed(v.Part, _runtime.ExecutionProvider)).Where(p => !p.LoadsOnDemand).DistinctBy(p => p.Name))
                await _runtime.PlacedAsync(_model, part.Name, ct);
            voice = await _runtime.HoldForAsync(_model, RequestPriority.Voice, ct);

            var host = _runtime.Host;
            var stream = await host.OpenStreamAsync(id => new Speak(id, _runtime.Ready(_model), options.Speed, RequestPriority.Voice), OwnedModelRuntime.StallLimit, ct);
            return new OwnedSpeakingSession(stream, use, voice);
        }
        catch (AiHostException ex)
        {
            if (voice is not null) await voice.DisposeAsync();
            use.Dispose();
            throw new LlmProviderException($"{Name} could not start speaking: {ex.Message}", ex);
        }
        catch
        {
            if (voice is not null) await voice.DisposeAsync();
            use.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        _runtime.Assets.Changed -= OnAssetChanged;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A stretch of speaking in the host: segments streamed in, their audio streamed back — its voices held at the voice
/// priority for its length.</summary>
internal sealed class OwnedSpeakingSession(IHostStream stream, IDisposable use, HostHold voice) : ITextToSpeechSession
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

    public async ValueTask DisposeAsync()
    {
        try { await stream.DisposeAsync(); }
        finally
        {
            await voice.DisposeAsync();
            use.Dispose();
        }
    }
}
