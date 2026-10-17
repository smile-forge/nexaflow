using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Audio;

/// <summary>Interleaved float samples with their layout — a recording, or a WAV read for playback.</summary>
public sealed record AudioClip(float[] Samples, int Channels, int Rate)
{
    public TimeSpan Length => Channels == 0 || Rate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Samples.Length / Channels / Rate);
}

/// <summary>Each channel's peak and RMS over the last twentieth of a second, in dBFS.</summary>
public sealed record AudioLevelReading(float[] PeakDb, float[] RmsDb);

/// <summary>
/// Plays a test signal on one endpoint, in shared mode at the engine's mix format, so a tone meant for "Side left"
/// reaches that channel and nothing else. Runs on its own MTA thread, as WASAPI wants.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AudioSignalPlayer
{
    /// <summary>Plays <paramref name="signal"/> until it ends — and has been heard to the end — or until
    /// <paramref name="ct"/> is cancelled, which stops it at once. Faults if the endpoint cannot be opened.</summary>
    public static Task PlayAsync(string endpointId, IAudioSignal signal, CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(endpointId, signal, done, ct)) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "test signal" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return done.Task;
    }

    static void Run(string endpointId, IAudioSignal signal, TaskCompletionSource done, CancellationToken ct)
    {
        IAudioClient2? client = null;
        IAudioRenderClient? render = null;
        try
        {
            client = WasapiStreams.OpenAtMixFormat(endpointId, bufferMs: 100, out var format);
            WasapiStreams.Check(client.GetBufferSize(out uint bufferFrames));
            render = WasapiStreams.Render(client);
            int channels = format.Channels;
            var scratch = new float[bufferFrames * channels];
            bool started = false, ended = false;

            while (!ct.IsCancellationRequested)
            {
                WasapiStreams.Check(client.GetCurrentPadding(out uint padding));
                if (ended)
                {
                    if (padding == 0) break;
                }
                else if (bufferFrames - padding is var room and > 0)
                {
                    int n = signal.Read(scratch.AsSpan(0, (int)room * channels), channels, format.Rate);
                    if (n > 0)
                    {
                        WasapiStreams.Check(render.GetBuffer((uint)n, out IntPtr data));
                        Marshal.Copy(scratch, 0, data, n * channels);
                        WasapiStreams.Check(render.ReleaseBuffer((uint)n, 0));
                        if (!started) { WasapiStreams.Check(client.Start()); started = true; }
                    }
                    if (n < room) ended = true;
                    if (ended && !started) break;
                }
                Thread.Sleep(10);
            }
            client.Stop();
            done.TrySetResult();
        }
        catch (Exception ex) { done.TrySetException(ex); }
        finally
        {
            if (render is not null) Marshal.ReleaseComObject(render);
            if (client is not null) Marshal.ReleaseComObject(client);
        }
    }
}

/// <summary>A recording endpoint being listened to: its levels as they arrive, and recordings of it on request.</summary>
public interface IAudioLevelMeter : IDisposable
{
    /// <summary>Channels and rate once started; zero before.</summary>
    int Channels { get; }
    int Rate { get; }

    /// <summary>Raised off the UI thread, twenty times a second.</summary>
    event Action<AudioLevelReading>? Reading;

    Task StartAsync();

    Task<AudioClip> RecordAsync(TimeSpan length, CancellationToken ct);
}

/// <summary>
/// Listens to one recording endpoint: every channel's level twenty times a second, and on request a recording of a
/// set length — both at the engine's mix format, so what is measured is what the device sent.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AudioLevelMeter(string endpointId) : IAudioLevelMeter
{
    readonly object _gate = new();
    readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Thread? _thread;
    volatile bool _running;
    Recording? _recording;

    sealed class Recording(int frames, int channels, int rate)
    {
        public readonly float[] Samples = new float[frames * channels];
        public readonly int Channels = channels, Rate = rate;
        public int Frames;
        public readonly TaskCompletionSource<AudioClip> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Channels and rate once started; zero before.</summary>
    public int Channels { get; private set; }
    public int Rate { get; private set; }

    /// <summary>Raised on the capture thread.</summary>
    public event Action<AudioLevelReading>? Reading;

    /// <summary>Opens the endpoint and starts listening; completes once samples are flowing, faults if it cannot open.</summary>
    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_thread is null)
            {
                _running = true;
                _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "level meter" };
                _thread.SetApartmentState(ApartmentState.MTA);
                _thread.Start();
            }
        }
        return _started.Task;
    }

    /// <summary>The next <paramref name="length"/> of what the endpoint hears, started now.</summary>
    public async Task<AudioClip> RecordAsync(TimeSpan length, CancellationToken ct)
    {
        await StartAsync().ConfigureAwait(false);
        var recording = new Recording((int)(length.TotalSeconds * Rate), Channels, Rate);
        lock (_gate)
        {
            if (_recording is not null) throw new InvalidOperationException("A recording is already running.");
            _recording = recording;
        }
        using var registration = ct.Register(() =>
        {
            lock (_gate) if (_recording == recording) _recording = null;
            recording.Done.TrySetCanceled(ct);
        });
        return await recording.Done.Task.ConfigureAwait(false);
    }

    void Run()
    {
        IAudioClient2? client = null;
        IAudioCaptureClient? capture = null;
        try
        {
            client = WasapiStreams.OpenAtMixFormat(endpointId, bufferMs: 100, out var format);
            (Channels, Rate) = (format.Channels, format.Rate);
            capture = WasapiStreams.Capture(client);
            WasapiStreams.Check(client.Start());
            _started.TrySetResult();

            int channels = format.Channels, blockFrames = Math.Max(format.Rate / 20, 1);
            var block = new float[blockFrames * channels];
            int filled = 0;
            var packet = new float[format.Rate * channels];
            while (_running)
            {
                Thread.Sleep(10);
                while (_running && capture.GetNextPacketSize(out uint next) == 0 && next > 0)
                {
                    WasapiStreams.Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    int samples = (int)frames * channels;
                    if (packet.Length < samples) packet = new float[samples];
                    if ((flags & 1) != 0) Array.Clear(packet, 0, samples);
                    else Marshal.Copy(data, packet, 0, samples);
                    WasapiStreams.Check(capture.ReleaseBuffer(frames));

                    Keep(packet.AsSpan(0, samples), channels);
                    for (int at = 0; at < samples;)
                    {
                        int take = Math.Min(block.Length - filled, samples - at);
                        packet.AsSpan(at, take).CopyTo(block.AsSpan(filled));
                        filled += take;
                        at += take;
                        if (filled == block.Length)
                        {
                            var peak = new float[channels];
                            var rms = new float[channels];
                            AudioLevels.Measure(block, channels, peak, rms);
                            Reading?.Invoke(new AudioLevelReading(peak, rms));
                            filled = 0;
                        }
                    }
                }
            }
            client.Stop();
        }
        catch (Exception ex)
        {
            _started.TrySetException(ex);
            lock (_gate) _recording?.Done.TrySetException(ex);
        }
        finally
        {
            if (capture is not null) Marshal.ReleaseComObject(capture);
            if (client is not null) Marshal.ReleaseComObject(client);
        }
    }

    /// <summary>Hands the packet to a recording in progress, finishing it once it has its length.</summary>
    void Keep(ReadOnlySpan<float> packet, int channels)
    {
        Recording? recording;
        lock (_gate) recording = _recording;
        if (recording is null) return;

        int frames = Math.Min(packet.Length / channels, recording.Samples.Length / channels - recording.Frames);
        packet[..(frames * channels)].CopyTo(recording.Samples.AsSpan(recording.Frames * channels));
        recording.Frames += frames;
        if (recording.Frames * channels < recording.Samples.Length) return;

        lock (_gate) if (_recording == recording) _recording = null;
        recording.Done.TrySetResult(new AudioClip(recording.Samples, recording.Channels, recording.Rate));
    }

    public void Dispose()
    {
        _running = false;
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            _recording?.Done.TrySetCanceled();
            _recording = null;
        }
        if (thread is not null && thread != Thread.CurrentThread) thread.Join();
        _started.TrySetCanceled();
    }
}
