namespace Nexaflow.Providers.Common;

/// <summary>
/// A text-to-speech model bound to a single model: text in, as segments arrive, and audio out as each is synthesised.
/// </summary>
public interface ITextToSpeechProvider : IAiProvider
{
    /// <summary>Whether it has a voice for <paramref name="language"/> (ISO 639-1). A reply in a language it cannot
    /// speak is shown rather than read out in the wrong voice.</summary>
    bool Speaks(string language);

    /// <summary>
    /// Opens a session that speaks the text given to it. The session holds whatever the backend needs for its life — a
    /// connection, a loaded model — and lets it go when disposed; disposing it before the end stops what is still
    /// being synthesised.
    /// </summary>
    Task<ITextToSpeechSession> StartSessionAsync(TextToSpeechOptions options, CancellationToken ct = default);
}

/// <param name="Speed">How fast to speak, 1 being the voice's own pace.</param>
public sealed record TextToSpeechOptions(float Speed = 1f);

/// <summary>
/// One stretch of speaking. Text goes in as segments, in the order they are to be heard — a reply's first clause,
/// then its sentences as they are written; <see cref="ReadAudioAsync"/> yields each segment's audio as it is made, and
/// ends once <see cref="CompleteAsync"/> has been called and the last segment is spoken. Failures surface from
/// <see cref="ReadAudioAsync"/> as an <see cref="LlmProviderException"/>.
/// </summary>
public interface ITextToSpeechSession : IAsyncDisposable
{
    /// <summary>The format every session yields: 24 kHz, 16-bit signed little-endian, mono — the rate of the models
    /// that make it.</summary>
    const int SampleRate = 24_000;

    /// <summary>Queues a segment to speak; returns its number, counting from 0, which its audio carries.</summary>
    /// <param name="language">The language it is written in, ISO 639-1 — which voice reads it; null for the
    /// session's first.</param>
    ValueTask<int> SpeakAsync(string text, string? language = null, CancellationToken ct = default);

    /// <summary>No more text is coming: speak what is queued, then end the audio.</summary>
    ValueTask CompleteAsync(CancellationToken ct = default);

    IAsyncEnumerable<TextToSpeechEvent> ReadAudioAsync(CancellationToken ct = default);
}

/// <summary>What a session yields. A closed set that grows by a record, like <see cref="SpeechToTextEvent"/>.</summary>
public abstract record TextToSpeechEvent;

/// <summary>More of a segment's audio, in <see cref="ITextToSpeechSession.SampleRate"/> 16-bit mono PCM.</summary>
public sealed record SpeechAudioChunk(int Segment, byte[] Pcm) : TextToSpeechEvent;

/// <summary>A segment's audio is complete — the point playback can map back to its text.</summary>
public sealed record SpeechSegmentEnd(int Segment) : TextToSpeechEvent;
