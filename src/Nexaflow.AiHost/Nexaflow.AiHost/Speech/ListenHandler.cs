using System.Diagnostics;
using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// Listens to the audio the app streams in: each frame through the voice activity detector, an utterance's frames
/// through the streaming recogniser, and each finished utterance through the transcriber — see
/// <see cref="ListenSession"/>. Replies <see cref="Progress"/> at least once a second, so a quiet room is not taken
/// for a hung host.
/// </summary>
internal sealed class ListenHandler(ModelRegistry models, GpuScheduler gpu) : HostStreamHandler<Listen>
{
    internal static readonly TimeSpan AliveInterval = TimeSpan.FromSeconds(1);

    protected override async Task HandleAsync(Listen request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct)
    {
        var parts = request.Models;
        if (request.End == UtteranceEnd.Turn && parts.TurnDetector is null)
            throw new HostRequestException(HostErrorKind.Invalid, "Listening for the end of a turn needs a turn detector.");

        Task Stage(string stage) => reply(new Progress(request.RequestId, stage, 0, 0));
        using var vad         = await models.AcquireAsync(parts.VoiceActivity, resident: false, Stage, ct);
        using var recognizer  = await models.AcquireAsync(parts.Recognizer, resident: false, Stage, ct);
        using var transcriber = parts.Transcriber is { } t ? await models.AcquireAsync(t, resident: false, Stage, ct) : null;
        using var turn        = parts.TurnDetector is { } d ? await models.AcquireAsync(d, resident: false, Stage, ct) : null;

        var streaming = recognizer.As<StreamingRecognizer>();
        using var session = new ListenSession(request.RequestId, request.Language, request.End,
            vad.As<SileroVoiceActivity>().Open(),
            () => streaming.Open(request.Language, gpu, request.Priority),
            transcriber?.As<WhisperTranscriber>().For(request.Language, gpu, request.Priority),
            turn?.As<SmartTurn>(),
            reply);

        var frames = new PcmFrames(ListenSession.FrameSamples);
        long heardSamples = 0;
        var alive = Stopwatch.GetTimestamp();
        Task Alive()
        {
            alive = Stopwatch.GetTimestamp();
            return reply(new Progress(request.RequestId, "listening", heardSamples * 1000 / ListenSession.SampleRate, 0));
        }

        while (true)
        {
            if (Stopwatch.GetElapsedTime(alive) >= AliveInterval) await Alive();
            if (!input.TryRead(out var item))
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(AliveInterval);
                try
                {
                    if (!await input.WaitToReadAsync(wait.Token)) break;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                continue;
            }

            if (item is not AudioInput audio) continue;
            foreach (var frame in frames.Add(audio.Pcm))
            {
                heardSamples += frame.Length;
                await session.FrameAsync(frame, ct);
            }
        }
        await session.FinishAsync(ct);
    }
}

/// <summary>16-bit little-endian PCM, however it is chunked, cut into frames of floats.</summary>
internal sealed class PcmFrames(int frameSamples)
{
    private readonly byte[] _pending = new byte[frameSamples * 2];
    private int _filled;

    public IEnumerable<float[]> Add(byte[] pcm)
    {
        int at = 0;
        while (at < pcm.Length)
        {
            int take = Math.Min(_pending.Length - _filled, pcm.Length - at);
            Buffer.BlockCopy(pcm, at, _pending, _filled, take);
            _filled += take;
            at      += take;
            if (_filled < _pending.Length) yield break;

            var frame = new float[frameSamples];
            for (int i = 0; i < frameSamples; i++) frame[i] = BitConverter.ToInt16(_pending, i * 2) / 32768f;
            _filled = 0;
            yield return frame;
        }
    }
}
