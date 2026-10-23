using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Audio;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// Speaks the text the app streams in (<see cref="TextToSpeak"/>), segment by segment and in order: each through its
/// language's phonemiser and voice, a GPU step per piece at the request's priority, its audio sent back in pieces of
/// about a second and its end marked. A segment in a language with no voice is read by the first.
/// </summary>
internal sealed class SpeakHandler(ModelRegistry models, GpuScheduler gpu, SpeechPhonemizers phonemizers) : HostStreamHandler<Speak>
{
    private const int ChunkSamples = KokoroSynthesizer.SampleRate;

    protected override async Task HandleAsync(Speak request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct)
    {
        if (request.Voices.Count == 0) throw new HostRequestException(HostErrorKind.Invalid, "Speaking needs a voice.");
        if (request.Voices.FirstOrDefault(v => !phonemizers.Reads(v.Language, v.Lexicon)) is { } unread)
            throw new HostRequestException(HostErrorKind.Invalid, $"This host cannot read {unread.Language} for a voice{(unread.Lexicon is null ? " without its lexicon" : "")}.");

        Task Stage(string stage) => reply(new Progress(request.RequestId, stage, 0, 0));
        var leases = new Dictionary<string, ModelRegistry.Lease>(StringComparer.Ordinal);

        // A voice's model is taken on with the first segment it reads: a language never spoken never loads its voice.
        async Task<KokoroSynthesizer> SynthesizerOf(VoiceSpec voice)
        {
            if (!leases.TryGetValue(voice.Model.Id, out var lease))
                leases[voice.Model.Id] = lease = await models.AcquireAsync(voice.Model, resident: false, Stage, ct);
            return lease.As<KokoroSynthesizer>();
        }

        try
        {
            await SynthesizerOf(request.Voices[0]);   // the first voice reads every language without one: ready before the first segment

            int spokenSamples = 0;
            Task Alive() => reply(new Progress(request.RequestId, "speaking", spokenSamples * 1000L / KokoroSynthesizer.SampleRate, 0));
            await foreach (var item in HostStreamInput.ReadAsync(input, Alive, ct))
            {
                if (item is not TextToSpeak text) continue;
                var voice = request.Voices.FirstOrDefault(v => string.Equals(v.Language, text.Language, StringComparison.OrdinalIgnoreCase)) ?? request.Voices[0];
                var synthesizer = await SynthesizerOf(voice);
                string phonemes = phonemizers.For(voice.Language, voice.Lexicon).Phonemize(text.Text);
                int? device = synthesizer.Spec.Device;
                using var speaking = gpu.Engage(request.Priority, device);   // a young job on its card waits while this is said
                foreach (var piece in synthesizer.Tokenize(phonemes))
                {
                    float[] wave;
                    using (await gpu.NextAsync(request.Priority, device, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        wave = synthesizer.Synthesize(piece, synthesizer.Voice(voice.VoiceFile), request.Speed);
                    }
                    for (int at = 0; at < wave.Length; at += ChunkSamples)
                        await reply(new SpokenAudio(request.RequestId, text.Segment, Pcm16.FromSamples(wave.AsSpan(at, Math.Min(ChunkSamples, wave.Length - at)))));
                    spokenSamples += wave.Length;
                }
                await reply(new SpokenSegmentEnd(request.RequestId, text.Segment));
            }
        }
        finally
        {
            foreach (var lease in leases.Values) lease.Dispose();
        }
    }
}
