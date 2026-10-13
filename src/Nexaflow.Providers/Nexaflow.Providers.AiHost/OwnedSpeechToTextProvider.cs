using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Speech to text Nexaflow runs itself, in its AI host: a voice activity detector, a streaming recogniser for the words
/// as they are spoken, a transcriber for the text that counts once an utterance ends, and a turn detector that tells a
/// finished turn from a thinking pause. Nothing leaves this machine.
/// </summary>
public sealed class OwnedSpeechToTextProvider : ISpeechToTextProvider, ILocalModelCatalog, IAsyncDisposable
{
    private readonly OwnedModelRuntime _runtime;
    private readonly OwnedModel?       _model;

    public OwnedSpeechToTextProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host,
                                     ComputeHardwareReport hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, hardware);
        _model   = OwnedModels.OfKind(ModelKind.SpeechToText).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
    }

    public string Name => OwnedLlmProvider.ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;

    public IReadOnlyList<CatalogModel> Catalog => [.. OwnedModels.OfKind(ModelKind.SpeechToText).Select(_runtime.CatalogOf)];

    public bool IsAvailable => _model is not null && _runtime.IsHere(_model);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. OwnedModels.OfKind(ModelKind.SpeechToText).Select(m => m.Id)]);

    public Task WarmupAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.WarmupAsync(_model, ct);

    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    /// <summary>Opens a stretch of listening in the host. Phrases end at each pause, as dictation wants; turns end when
    /// the speaker has finished, and are heard at the voice priority, ahead of everything else on the GPU.</summary>
    public async Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{Name} has no speech model of that name.");
        var models = new ListenModels(
            _runtime.Spec(_model, OwnedRoles.VoiceActivity),
            _runtime.Spec(_model, OwnedRoles.Recognizer),
            _runtime.Spec(_model, OwnedRoles.Transcriber),
            _runtime.Spec(_model, OwnedRoles.TurnDetector));
        var (end, priority) = options.Segmentation == SpeechSegmentation.Turns
            ? (UtteranceEnd.Turn, RequestPriority.Voice)
            : (UtteranceEnd.Pause, RequestPriority.Interactive);

        var host = _runtime.Host;
        host.Environment = _runtime.Environment;
        try
        {
            var stream = await host.OpenStreamAsync(id => new Listen(id, models, options.Language, end, priority), OwnedModelRuntime.StallLimit, ct);
            return new OwnedListeningSession(stream);
        }
        catch (AiHostException ex)
        {
            throw new LlmProviderException($"{Name} could not start listening: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _runtime.Assets.Changed -= OnAssetChanged;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A stretch of listening in the host: captured audio streamed in, what is heard streamed back.</summary>
internal sealed class OwnedListeningSession(IHostStream stream) : ISpeechToTextSession
{
    public async ValueTask AppendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default)
    {
        try { await stream.WriteAsync(new AudioInput(stream.RequestId, pcm.ToArray()), ct); }
        catch (AiHostException ex) { throw new LlmProviderException($"Listening stopped: {ex.Message}", ex); }
    }

    public async ValueTask CompleteAsync(CancellationToken ct = default)
    {
        try { await stream.WriteAsync(new InputEnd(stream.RequestId), ct); }
        catch (AiHostException ex) { throw new LlmProviderException($"Listening stopped: {ex.Message}", ex); }
    }

    public async IAsyncEnumerable<SpeechToTextEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct = default)
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
                throw new LlmProviderException($"Listening stopped: {ex.Message}", ex);
            }
            switch (replies.Current)
            {
                case ListenActivity activity: yield return new SpeechActivity(activity.Speaking); break;
                case ListenPartial partial:   yield return new TranscriptPartial(partial.Text); break;
                case ListenFinal final:       yield return new TranscriptFinal(final.Text); break;
            }
        }
    }

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
