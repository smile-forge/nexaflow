using Nexaflow.AiHost.Catalog;

namespace Nexaflow.AiHost.Speech;

/// <summary>The parts one stretch of listening runs on, each where it runs — from the catalog, for the model a listen names.</summary>
/// <param name="VoiceActivity">Hears where speech begins and ends.</param>
/// <param name="Recognizer">Streams the words as they are spoken.</param>
/// <param name="Transcriber">Transcribes each finished utterance once more, whole — the text that counts. Null keeps
/// the recognizer's.</param>
/// <param name="TurnDetector">Judges from the audio whether a pause ends the speaker's turn; needed to listen for turns.</param>
internal sealed record ListenModels(ModelSpec VoiceActivity, ModelSpec Recognizer, ModelSpec? Transcriber, ModelSpec? TurnDetector);

/// <summary>A voice: the language it reads, the part that speaks it, and its file among the part's.</summary>
/// <param name="Lexicon">Where the lexicon its language is read from lies, for a language read from one (French, German); null for
/// one the host reads without.</param>
internal sealed record VoiceSpec(string Language, ModelSpec Model, string VoiceFile, string? Lexicon = null);
