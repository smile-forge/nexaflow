using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services;

/// <summary>A whole recording put through a speech-to-text model: what a model that cannot hear is given instead.</summary>
internal static class SpeechTranscription
{
    /// <summary>100 ms of audio at the sessions' rate — appended a slice at a time, as a microphone would.</summary>
    private const int Slice = ISpeechToTextSession.SampleRate / 10 * 2;

    /// <summary>The words in <paramref name="wav"/>, as <paramref name="model"/> hears them; empty when it hears none.</summary>
    /// <exception cref="InvalidDataException">It is not a WAV the session's format can be made from.</exception>
    public static async Task<string> TranscribeAsync(ISpeechToTextProvider model, byte[] wav, CancellationToken ct)
    {
        var (pcm, _) = WavAudio.ReadMono(WavAudio.ToMono(wav, ISpeechToTextSession.SampleRate));
        await using var session = await model.StartSessionAsync(new SpeechToTextOptions(), ct);

        var heard = new List<string>();
        var reading = Task.Run(async () =>
        {
            await foreach (var e in session.ReadEventsAsync(ct))
                if (e is TranscriptFinal { Text: var text } && !string.IsNullOrWhiteSpace(text)) heard.Add(text.Trim());
        }, ct);

        for (int at = 0; at < pcm.Length; at += Slice)
            await session.AppendAudioAsync(pcm.AsMemory(at, Math.Min(Slice, pcm.Length - at)), ct);
        await session.CompleteAsync(ct);
        await reading;
        return string.Join(" ", heard);
    }
}
