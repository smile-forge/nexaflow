namespace Nexaflow.Providers.Common;

/// <summary>
/// A speech-to-text model bound to a single model: audio streamed in, transcripts out as the speaker talks.
/// </summary>
public interface ISpeechToTextProvider : IAiProvider
{
    /// <summary>
    /// Opens a session that transcribes the audio appended to it. The session holds whatever the backend needs
    /// for its life — a connection, a loaded model — and lets it go when disposed.
    /// </summary>
    Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default);
}

/// <param name="Language">The language being spoken, as an ISO 639-1 code ("en", "fr", "de"); null lets the model
/// detect it. Short utterances are misheard when it has to guess, so callers that know should say.</param>
public sealed record SpeechToTextOptions(string? Language = null);

/// <summary>
/// One stretch of listening. Audio goes in as it is captured; <see cref="ReadEventsAsync"/> yields what the model
/// makes of it, and ends once <see cref="CompleteAsync"/> has been called and the last of the audio is transcribed.
/// Failures surface from <see cref="ReadEventsAsync"/> as an <see cref="LlmProviderException"/>.
/// </summary>
public interface ISpeechToTextSession : IAsyncDisposable
{
    /// <summary>The sample format every session takes: 16 kHz, 16-bit signed little-endian, mono.</summary>
    const int SampleRate = 16_000;

    /// <summary>Adds captured audio, in <see cref="SampleRate"/> 16-bit mono PCM.</summary>
    ValueTask AppendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default);

    /// <summary>No more audio is coming: transcribe what is left, then end the events.</summary>
    ValueTask CompleteAsync(CancellationToken ct = default);

    IAsyncEnumerable<SpeechToTextEvent> ReadEventsAsync(CancellationToken ct = default);
}

/// <summary>What a session reports. A closed set that grows by a record, like <see cref="LlmStreamEvent"/>.</summary>
public abstract record SpeechToTextEvent;

/// <summary>The current utterance so far — the whole of it, revised as more audio arrives.</summary>
public sealed record TranscriptPartial(string Text) : SpeechToTextEvent;

/// <summary>An utterance is finished; its text will not change. Later ones follow it.</summary>
public sealed record TranscriptFinal(string Text) : SpeechToTextEvent;

/// <summary>The model heard speech begin, or end — for a backend that detects it.</summary>
public sealed record SpeechActivity(bool Speaking) : SpeechToTextEvent;
