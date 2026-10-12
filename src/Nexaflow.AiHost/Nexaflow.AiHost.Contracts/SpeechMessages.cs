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
