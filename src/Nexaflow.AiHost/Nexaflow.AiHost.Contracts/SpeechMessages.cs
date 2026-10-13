namespace Nexaflow.AiHost.Contracts;

/// <summary>Where a stretch of listening cuts one utterance from the next.</summary>
public enum UtteranceEnd
{
    /// <summary>At every pause — dictation, where each phrase becomes text as soon as the speaker stops.</summary>
    Pause,

    /// <summary>When the speaker has finished their turn — a conversation, which must not answer a thinking pause.</summary>
    Turn,
}

/// <summary>The models one stretch of listening runs on.</summary>
/// <param name="VoiceActivity">Hears where speech begins and ends.</param>
/// <param name="Recognizer">Streams the words as they are spoken.</param>
/// <param name="Transcriber">Transcribes each finished utterance once more, whole — the text that counts. Null keeps
/// the recognizer's.</param>
/// <param name="TurnDetector">Judges from the audio whether a pause ends the speaker's turn; needed to listen for
/// <see cref="UtteranceEnd.Turn"/>.</param>
public sealed record ListenModels(ModelSpec VoiceActivity, ModelSpec Recognizer, ModelSpec? Transcriber, ModelSpec? TurnDetector);

/// <summary>
/// Listens to the audio sent into it (<see cref="AudioInput"/>) until <see cref="InputEnd"/>: replies
/// <see cref="ListenActivity"/> as speech starts and pauses, <see cref="ListenPartial"/> as words are recognised, and
/// <see cref="ListenFinal"/> as each utterance ends. While no audio arrives it replies <see cref="Progress"/> each
/// second, so a quiet room is not taken for a hung host.
/// </summary>
/// <param name="Language">The language being spoken, as an ISO 639-1 code; null lets the models detect it, which
/// mishears short utterances.</param>
public sealed record Listen(long RequestId, ListenModels Models, string? Language, UtteranceEnd End, RequestPriority Priority)
    : HostRequest(RequestId);

/// <summary>Speech started — after enough of it to be more than a click — or paused.</summary>
public sealed record ListenActivity(long RequestId, bool Speaking) : HostReply(RequestId);

/// <summary>The current utterance so far, revised as more is heard.</summary>
public sealed record ListenPartial(long RequestId, string Text) : HostReply(RequestId);

/// <summary>An utterance has ended; its text will not change.</summary>
public sealed record ListenFinal(long RequestId, string Text) : HostReply(RequestId);

/// <summary>A voice: the language it reads, the model that speaks it, and its file among the model's.</summary>
/// <param name="Lexicon">The lexicon its language is read from — a path on this machine — for a language read from one
/// (French, German); null for one the host reads without.</param>
public sealed record VoiceSpec(string Language, ModelSpec Model, string VoiceFile, string? Lexicon = null);

/// <summary>
/// Speaks the text sent into it (<see cref="TextToSpeak"/>) until <see cref="InputEnd"/>: each segment in order, in
/// the voice for its language, replied as <see cref="SpokenAudio"/> — 24 kHz 16-bit mono PCM — and then
/// <see cref="SpokenSegmentEnd"/>.
/// </summary>
/// <param name="Voices">The voices it may use; the first reads anything in a language none of them is for.</param>
/// <param name="Speed">How fast, 1 being each voice's own pace.</param>
public sealed record Speak(long RequestId, IReadOnlyList<VoiceSpec> Voices, float Speed, RequestPriority Priority) : HostRequest(RequestId);

/// <summary>A segment to speak, numbered by the app in the order it is to be heard.</summary>
/// <param name="Language">ISO 639-1; null for the first voice's.</param>
public sealed record TextToSpeak(long RequestId, int Segment, string Text, string? Language) : RequestInput(RequestId);

public sealed record SpokenAudio(long RequestId, int Segment, byte[] Pcm) : HostReply(RequestId);

public sealed record SpokenSegmentEnd(long RequestId, int Segment) : HostReply(RequestId);
