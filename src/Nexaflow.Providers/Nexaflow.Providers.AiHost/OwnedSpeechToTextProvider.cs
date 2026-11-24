using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Speech to text Nexaflow runs itself, in its AI host: a voice activity detector, a streaming recogniser for the words
/// as they are spoken, a transcriber for the text that counts once an utterance ends, and a turn detector that tells a
/// finished turn from a thinking pause. Nothing leaves this machine.
/// </summary>
public sealed class OwnedSpeechToTextProvider : ISpeechToTextProvider, ILocalModelCatalog, IHostModelCatalog, ITracksModelUse, IAsyncDisposable
{
    private readonly OwnedModelRuntime _runtime;
    private readonly HostedModel?       _model;

    public OwnedSpeechToTextProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostHolds holds,
                                     MachineCards hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, holds, hardware);
        _model   = OwnedModels.OfKind(ModelKind.SpeechToText).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
        _runtime.ReachabilityChanged += (_, _) => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Name => OwnedLlmProvider.ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;

    public IReadOnlyList<CatalogModel> Catalog => [.. _runtime.Runnable(ModelKind.SpeechToText).Select(_runtime.CatalogOf)];

    public IReadOnlyList<UnrunnableModel> Unrunnable => [.. _runtime.Unrunnable(ModelKind.SpeechToText)];

    public LocalRuntime? RuntimeOf(string modelId) => _runtime.Runtime;

    public bool LoadsOnDemand => false;

    public IReadOnlyList<PlacementNeed> NeedsOf(string modelId)
        => OwnedModels.OfKind(ModelKind.SpeechToText).FirstOrDefault(m => m.Id == modelId) is { } model ? _runtime.NeedsOf(model) : [];

    public bool IsAvailable => _model is not null && _runtime.Reachable && _runtime.IsHere(_model);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. _runtime.Runnable(ModelKind.SpeechToText).Select(m => m.Id)]);

    public Task WarmupAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.WarmupAsync(_model, ct);

    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    public ModelUseTracker ModelUse => _runtime.ModelUse;

    /// <summary>Opens a stretch of listening in the host. Phrases end at each pause, as dictation wants; turns end when
    /// the speaker has finished, and are heard at the voice priority, ahead of everything else on the GPU.</summary>
    public async Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{Name} has no speech model of that name.");
        var use = await _runtime.ModelUse.BeginAsync(ct);   // the session's whole stretch is one use, ended with it
        HostHold? voice = null;
        try
        {
            foreach (var role in new[] { HostedRoles.VoiceActivity, HostedRoles.Recognizer, HostedRoles.Transcriber, HostedRoles.TurnDetector })
                await _runtime.PlacedAsync(_model, role, ct);
            var (end, priority) = options.Segmentation == SpeechSegmentation.Turns
                ? (UtteranceEnd.Turn, RequestPriority.Voice)
                : (UtteranceEnd.Pause, RequestPriority.Interactive);
            // A conversation's listening is held at the voice priority for its length: placed ahead of every other held model.
            if (priority == RequestPriority.Voice) voice = await _runtime.HoldForAsync(_model, priority, ct);

            var host = _runtime.Host;
            var stream = await host.OpenStreamAsync(id => new Listen(id, _model.Id, options.Language, end, priority), OwnedModelRuntime.StallLimit, ct);
            return new OwnedListeningSession(stream, use, voice);
        }
        catch (AiHostException ex)
        {
            if (voice is not null) await voice.DisposeAsync();
            use.Dispose();
            throw new LlmProviderException($"{Name} could not start listening: {ex.Message}", ex);
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

/// <summary>A stretch of listening in the host: captured audio streamed in, what is heard streamed back — its models held at the
/// voice priority for its length, in a conversation.</summary>
internal sealed class OwnedListeningSession(IHostStream stream, IDisposable use, HostHold? voice) : ISpeechToTextSession
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

    public async ValueTask DisposeAsync()
    {
        try { await stream.DisposeAsync(); }
        finally
        {
            if (voice is not null) await voice.DisposeAsync();
            use.Dispose();
        }
    }
}
