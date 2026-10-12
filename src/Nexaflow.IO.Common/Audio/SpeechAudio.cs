using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;

namespace Nexaflow.IO.Common.Audio;

/// <summary>The microphone for a spoken conversation: 16 kHz 16-bit mono PCM as it is captured.</summary>
public interface ISpeechCapture : IDisposable
{
    const int SampleRate = 16_000;

    /// <summary>Opens the device and starts capturing; faults when there is no microphone.</summary>
    Task StartAsync();

    /// <summary>Captured audio in pieces of about 10 ms. Completes when capture stops.</summary>
    ChannelReader<byte[]> Audio { get; }

    /// <summary>Whether Windows reports echo cancellation on the stream — without it, the assistant hears itself.</summary>
    bool CancelsEcho { get; }
}

/// <summary>
/// The speaker for a spoken conversation: 24 kHz 16-bit mono PCM queued and played in order, pausable on the spot and
/// stoppable at once. Its counts are of samples since it was made: what was queued, and what has been heard — so a
/// stopped reply can be mapped back to the words that reached the listener.
/// </summary>
public interface ISpeechPlayback : IDisposable
{
    const int SampleRate = 24_000;

    /// <summary>The playback endpoint — the reference the capture's echo canceller subtracts.</summary>
    string EndpointId { get; }

    long QueuedSamples { get; }

    long PlayedSamples { get; }

    bool IsPaused { get; }

    void Enqueue(byte[] pcm);

    /// <summary>Holds playback where it is; <see cref="Resume"/> carries on from the same sample.</summary>
    void Pause();

    void Resume();

    /// <summary>Stops at once and drops what was still queued: what was queued becomes what was heard.</summary>
    void Stop();

    /// <summary>Everything queued has been heard. Raised on the playback thread.</summary>
    event EventHandler? Drained;
}

/// <summary>
/// Capture through Windows' communications path — echo cancellation, noise suppression and gain control, which took
/// ~50 dB of the assistant's own voice off a webcam microphone over monitor speakers, leaving the recogniser nothing to
/// hear of it — with the playback endpoint as the echo canceller's reference. COM is opened and polled on a thread of
/// its own.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CommunicationsCapture(string echoReferenceEndpointId) : ISpeechCapture
{
    private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private volatile bool _running;

    public ChannelReader<byte[]> Audio => _audio.Reader;

    public bool CancelsEcho { get; private set; }

    public Task StartAsync()
    {
        if (_thread is not null) return _started.Task;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "speech capture" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        return _started.Task;
    }

    private void Run()
    {
        IAudioClient2? client = null;
        IAudioCaptureClient? capture = null;
        try
        {
            string microphone = WasapiStreams.DefaultEndpoint(WasapiStreams.CaptureFlow, WasapiStreams.CommunicationsRole);
            client  = WasapiStreams.Open(microphone, communications: true, ISpeechCapture.SampleRate, bufferMs: 100);
            WasapiStreams.SetEchoReference(client, echoReferenceEndpointId);
            CancelsEcho = WasapiStreams.CancelsEcho(client);
            capture = WasapiStreams.Capture(client);
            WasapiStreams.Check(client.Start());
            _started.TrySetResult();

            var samples = new float[ISpeechCapture.SampleRate / 10];
            while (_running)
            {
                Thread.Sleep(5);
                while (capture.GetNextPacketSize(out uint packet) == 0 && packet > 0)
                {
                    WasapiStreams.Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    int n = (int)frames;
                    if (samples.Length < n) samples = new float[n];
                    if ((flags & 1) != 0) Array.Clear(samples, 0, n);   // AUDCLNT_BUFFERFLAGS_SILENT
                    else Marshal.Copy(data, samples, 0, n);
                    WasapiStreams.Check(capture.ReleaseBuffer(frames));
                    _audio.Writer.TryWrite(Pcm16.FromSamples(samples.AsSpan(0, n)));
                }
            }
            client.Stop();
            _audio.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _started.TrySetException(ex);
            _audio.Writer.TryComplete(ex);
        }
        finally
        {
            if (capture is not null) Marshal.ReleaseComObject(capture);
            if (client is not null) Marshal.ReleaseComObject(client);
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_thread is { IsAlive: true } && _thread != Thread.CurrentThread) _thread.Join();
    }
}

/// <summary>
/// Playback on the default speakers through a shared-mode stream with a 60 ms buffer, fed from a queue on a thread of
/// its own: a pause takes effect at the next buffer, a stop at once — the speaker is 6 dB down about 50 ms later. The
/// stream's clock says how much has been heard.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SpeakerPlayback : ISpeechPlayback
{
    private readonly object _gate = new();
    private readonly Queue<float[]> _queue = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private float[]? _current;
    private int _currentAt;
    private long _queued;
    private long _played;          // heard before the stream's clock was last reset
    private long _clockSamples;    // heard since then, by the stream's clock
    private bool _paused, _stopRequested, _streaming, _disposed;

    public SpeakerPlayback()
    {
        EndpointId = WasapiStreams.DefaultEndpoint(WasapiStreams.RenderFlow, WasapiStreams.ConsoleRole);
        _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "speech playback" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _opened.Task.GetAwaiter().GetResult();
    }

    public string EndpointId { get; }

    public long QueuedSamples { get { lock (_gate) return _queued; } }

    public long PlayedSamples { get { lock (_gate) return _played + _clockSamples; } }

    public bool IsPaused { get { lock (_gate) return _paused; } }

    public event EventHandler? Drained;

    public void Enqueue(byte[] pcm)
    {
        var samples = Pcm16.ToSamples(pcm);
        lock (_gate)
        {
            _queue.Enqueue(samples);
            _queued += samples.Length;
        }
    }

    public void Pause()   { lock (_gate) _paused = true; }

    public void Resume()  { lock (_gate) _paused = false; }

    public void Stop()    { lock (_gate) _stopRequested = true; }

    private void Run()
    {
        IAudioClient2? client = null;
        IAudioRenderClient? render = null;
        IAudioClock? clock = null;
        try
        {
            client = WasapiStreams.Open(EndpointId, communications: false, ISpeechPlayback.SampleRate, bufferMs: 60);
            WasapiStreams.Check(client.GetBufferSize(out uint bufferFrames));
            render = WasapiStreams.Render(client);
            clock  = WasapiStreams.Clock(client);
            WasapiStreams.Check(clock.GetFrequency(out ulong frequency));
            _opened.TrySetResult();

            bool started = false;
            while (true)
            {
                Thread.Sleep(5);
                bool stop, pause;
                lock (_gate)
                {
                    if (_disposed) break;
                    stop  = _stopRequested;
                    pause = _paused;
                }

                if (stop)
                {
                    client.Stop();
                    client.Reset();
                    started = false;
                    lock (_gate)
                    {
                        _played += _clockSamples;
                        _clockSamples = 0;
                        _queue.Clear();
                        _current = null;
                        _queued = _played;
                        _stopRequested = _paused = _streaming = false;
                    }
                    continue;
                }

                if (pause)
                {
                    if (started) { client.Stop(); started = false; }
                    continue;
                }

                Fill(client, render, bufferFrames);
                WasapiStreams.Check(client.GetCurrentPadding(out uint padding));
                if (padding > 0 && !started) { WasapiStreams.Check(client.Start()); started = true; }
                if (clock.GetPosition(out ulong position, out _) == 0)
                    lock (_gate) _clockSamples = (long)(position * (ulong)ISpeechPlayback.SampleRate / frequency);

                bool drained;
                lock (_gate) drained = _streaming && padding == 0 && _current is null && _queue.Count == 0;
                if (drained)
                {
                    client.Stop();
                    client.Reset();
                    started = false;
                    lock (_gate)
                    {
                        _played = _queued;
                        _clockSamples = 0;
                        _streaming = false;
                    }
                    Drained?.Invoke(this, EventArgs.Empty);
                }
            }
            client.Stop();
        }
        catch (Exception ex)
        {
            _opened.TrySetException(ex);
        }
        finally
        {
            if (clock is not null) Marshal.ReleaseComObject(clock);
            if (render is not null) Marshal.ReleaseComObject(render);
            if (client is not null) Marshal.ReleaseComObject(client);
        }
    }

    /// <summary>Writes as much of the queue as the stream's buffer has room for.</summary>
    private void Fill(IAudioClient2 client, IAudioRenderClient render, uint bufferFrames)
    {
        WasapiStreams.Check(client.GetCurrentPadding(out uint padding));
        int room = (int)(bufferFrames - padding);
        while (room > 0)
        {
            float[] samples;
            int at;
            lock (_gate)
            {
                if (_current is null || _currentAt >= _current.Length)
                {
                    if (!_queue.TryDequeue(out _current)) { _current = null; return; }
                    _currentAt = 0;
                    _streaming = true;
                }
                samples = _current;
                at = _currentAt;
            }
            int n = Math.Min(room, samples.Length - at);
            WasapiStreams.Check(render.GetBuffer((uint)n, out IntPtr data));
            Marshal.Copy(samples, at, data, n);
            WasapiStreams.Check(render.ReleaseBuffer((uint)n, 0));
            room -= n;
            lock (_gate)
            {
                if (_current == samples) _currentAt = at + n;
                if (_currentAt >= samples.Length) _current = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        if (_thread.IsAlive && _thread != Thread.CurrentThread) _thread.Join();
    }
}
