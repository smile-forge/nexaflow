using System.Threading.Channels;
using Nexaflow.Providers.Common;
using Whisper.net;

namespace Nexaflow.Providers.Whisper;

/// <summary>
/// A listening session on whisper.cpp, which transcribes a buffer rather than a stream: while audio arrives, the whole
/// of it is transcribed again every <see cref="PartialInterval"/> and reported as the utterance so far; when the
/// audio ends, one last pass is the final text. Whisper sees the whole utterance each time, so a partial corrects
/// itself as more is heard.
/// </summary>
internal sealed class WhisperSession : ISpeechToTextSession
{
    internal static readonly TimeSpan PartialInterval = TimeSpan.FromSeconds(1);

    /// <summary>Under a third of a second, whisper.cpp has nothing to say.</summary>
    private const int MinSamples = ISpeechToTextSession.SampleRate / 3;

    private readonly WhisperFactory _factory;
    private readonly string         _language;
    private readonly List<float>    _samples = [];
    private readonly Lock           _samplesLock = new();
    private readonly SemaphoreSlim  _pass = new(1, 1);
    private readonly Channel<SpeechToTextEvent> _events = Channel.CreateUnbounded<SpeechToTextEvent>();
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _partials;

    private readonly IDisposable _use;

    /// <param name="use">The provider's use of its model, ended once this session has stopped transcribing.</param>
    public WhisperSession(WhisperFactory factory, string language, IDisposable use)
    {
        _factory  = factory;
        _language = language;
        _use      = use;
        _partials = Task.Run(PartialsAsync);
    }

    public ValueTask AppendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default)
    {
        var span = pcm.Span;
        lock (_samplesLock)
            for (var i = 0; i + 1 < span.Length; i += 2)
                _samples.Add((short)(span[i] | (span[i + 1] << 8)) / 32768f);
        return ValueTask.CompletedTask;
    }

    public async ValueTask CompleteAsync(CancellationToken ct = default)
    {
        await _closing.CancelAsync();
        try { await _partials; } catch (OperationCanceledException) { }

        try
        {
            var text = await TranscribeAsync(ct);
            if (text.Length > 0) _events.Writer.TryWrite(new TranscriptFinal(text));
            _events.Writer.TryComplete();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _events.Writer.TryComplete(new LlmProviderException($"Whisper transcription failed: {ex.Message}", ex));
        }
    }

    public IAsyncEnumerable<SpeechToTextEvent> ReadEventsAsync(CancellationToken ct = default) => _events.Reader.ReadAllAsync(ct);

    private async Task PartialsAsync()
    {
        while (!_closing.IsCancellationRequested)
        {
            await Task.Delay(PartialInterval, _closing.Token);
            try
            {
                var text = await TranscribeAsync(_closing.Token);
                if (text.Length > 0 && !_closing.IsCancellationRequested) _events.Writer.TryWrite(new TranscriptPartial(text));
            }
            catch (OperationCanceledException) { throw; }
            catch { /* a failed partial is retried by the next; the final pass reports a real failure */ }
        }
    }

    /// <summary>Everything heard so far, as one transcription. Passes do not overlap: whisper.cpp holds one model.</summary>
    private async Task<string> TranscribeAsync(CancellationToken ct)
    {
        float[] samples;
        lock (_samplesLock) samples = [.. _samples];
        if (samples.Length < MinSamples) return string.Empty;

        await _pass.WaitAsync(ct);
        try
        {
            await using var processor = _factory.CreateBuilder().WithLanguage(_language).Build();
            var text = new System.Text.StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct))
                text.Append(segment.Text);
            return text.ToString().Trim();
        }
        finally { _pass.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _closing.CancelAsync();
        try { await _partials; } catch (OperationCanceledException) { }
        _events.Writer.TryComplete();
        _closing.Dispose();
        _pass.Dispose();
        _use.Dispose();
    }
}
