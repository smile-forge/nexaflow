using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.Providers.Common;
using OpenAI;
using OpenAI.Audio;

namespace Nexaflow.Providers.OpenAI;

#pragma warning disable OPENAI001 // streamed speech is marked for evaluation in the SDK

/// <summary>
/// OpenAI's speech models (gpt-4o-mini-tts, tts-1, tts-1-hd): each segment is sent as it arrives and its audio comes
/// back as 24 kHz PCM — streamed as it is made by the models that stream, whole from those that do not. Its voices read
/// any language the model knows.
/// </summary>
public sealed class OpenAITextToSpeechProvider : ITextToSpeechProvider
{
    /// <summary>The voice every session speaks in.</summary>
    internal const string Voice = "coral";

    private readonly OpenAIConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private OpenAIClient?                       _client;

    public OpenAITextToSpeechProvider(IBackgroundActivityManager activityManager, OpenAIConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    public string Name => OpenAILlmProvider.ProviderName;

    public string? LastModelListError { get; private set; }

    private OpenAIClient Client => _client ??= OpenAIClients.Create(_config);

    public DataResidency Residency => EndpointResidency.Of(_config.BaseUrl, DataResidency.LeavesMachine);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    public bool Speaks(string language) => true;

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await Client.GetOpenAIModelClient().GetModelsAsync(ct);
            LastModelListError = null;
            return result.Value.Select(m => m.Id).Where(IsTextToSpeech).Order().ToList();
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    /// <summary>The models the speech endpoint takes.</summary>
    internal static bool IsTextToSpeech(string model) => model.Contains("tts", StringComparison.OrdinalIgnoreCase);

    /// <summary>The gpt-4o family streams its audio as it is made; tts-1 answers whole.</summary>
    internal static bool Streams(string model) => model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase);

    public Task<ITextToSpeechSession> StartSessionAsync(TextToSpeechOptions options, CancellationToken ct = default)
    {
        var activity = _activityManager.StartActivity($"OpenAI speech ({_model})…");
        var audio = Client.GetAudioClient(_model);
        var generation = new SpeechGenerationOptions { ResponseFormat = GeneratedSpeechFormat.Pcm, SpeedRatio = options.Speed };
        return Task.FromResult<ITextToSpeechSession>(new OpenAISpeakingSession(audio, generation, Streams(_model), activity));
    }
}

/// <summary>
/// One stretch of speaking: segments are synthesised one after another, in order, on a loop of their own, and their
/// audio queued for the reader.
/// </summary>
internal sealed class OpenAISpeakingSession : ITextToSpeechSession
{
    private readonly Channel<(int Segment, string Text)> _text = Channel.CreateUnbounded<(int, string)>(new() { SingleReader = true });
    private readonly Channel<TextToSpeechEvent> _audio = Channel.CreateUnbounded<TextToSpeechEvent>(new() { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _speaking;
    private int _segments;

    public OpenAISpeakingSession(AudioClient client, SpeechGenerationOptions options, bool streams, IActivityHandle activity)
    {
        _speaking = Task.Run(() => SpeakAllAsync(client, options, streams, activity));
    }

    public async ValueTask<int> SpeakAsync(string text, string? language = null, CancellationToken ct = default)
    {
        int segment = _segments++;
        await _text.Writer.WriteAsync((segment, text), ct);
        return segment;
    }

    public ValueTask CompleteAsync(CancellationToken ct = default)
    {
        _text.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<TextToSpeechEvent> ReadAudioAsync(CancellationToken ct = default) => ReadAsync(ct);

    private async IAsyncEnumerable<TextToSpeechEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var e in _audio.Reader.ReadAllAsync(ct)) yield return e;
    }

    private async Task SpeakAllAsync(AudioClient client, SpeechGenerationOptions options, bool streams, IActivityHandle activity)
    {
        var voice = new GeneratedSpeechVoice(OpenAITextToSpeechProvider.Voice);
        try
        {
            await foreach (var (segment, text) in _text.Reader.ReadAllAsync(_stop.Token))
            {
                if (streams)
                {
                    var whole = new SampleAligner();
                    await foreach (var update in client.GenerateSpeechStreamingAsync(text, voice, options, _stop.Token))
                        if (update is StreamingSpeechAudioDeltaUpdate delta && whole.Take(delta.AudioBytes.ToArray()) is { Length: > 0 } pcm)
                            await _audio.Writer.WriteAsync(new SpeechAudioChunk(segment, pcm), _stop.Token);
                }
                else
                {
                    var result = await client.GenerateSpeechAsync(text, voice, options, _stop.Token);
                    await _audio.Writer.WriteAsync(new SpeechAudioChunk(segment, result.Value.ToArray()), _stop.Token);
                }
                await _audio.Writer.WriteAsync(new SpeechSegmentEnd(segment), _stop.Token);
            }
            activity.Complete();
            _audio.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            activity.Complete();
            _audio.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            _audio.Writer.TryComplete(new LlmProviderException($"OpenAI speech failed: {ex.Message}", ex));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _text.Writer.TryComplete();
        try { await _speaking; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }

    /// <summary>Streamed audio may split a sample between deltas; this keeps the odd byte for the next.</summary>
    private sealed class SampleAligner
    {
        private byte? _held;

        public byte[] Take(byte[] bytes)
        {
            byte[] all = _held is { } h ? [h, .. bytes] : bytes;
            _held = all.Length % 2 == 1 ? all[^1] : null;
            return all.Length % 2 == 1 ? all[..^1] : all;
        }
    }
}
