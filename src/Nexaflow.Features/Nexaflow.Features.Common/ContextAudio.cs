namespace Nexaflow.Features.Common;

/// <summary>
/// A recording produced by a client tool for the model to hear — the audio player's "listen to the track" tool
/// returning a stretch of it. The agent harness forwards it to the next model turn as audio when the conversation
/// model can hear; otherwise the workspace's Speech In model transcribes it and the transcript goes instead.
/// </summary>
/// <param name="Wav">The recording as a PCM WAV file.</param>
/// <param name="Label">Optional short description (e.g. the track and where in it) for naming.</param>
public sealed record ContextAudio(byte[] Wav, string? Label = null);
