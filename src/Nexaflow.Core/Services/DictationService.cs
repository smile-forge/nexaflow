using System.Text;
using System.Threading.Channels;
using NAudio.Wave;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// Dictation into the AI bar: the microphone, captured at 16 kHz mono, streamed into the speech-to-text model the
/// workspace's Speech In chain picks, with the text growing as the model hears it. Listening stops by itself once
/// speech has been heard and then <see cref="SilenceTimeout"/> of quiet, or when the user stops it.
/// <para>Events are raised on the thread that caused them — capture, or the model's session — so a listener marshals.</para>
/// </summary>
public sealed class DictationService
{
    private static readonly WaveFormat CaptureFormat = new(ISpeechToTextSession.SampleRate, 16, 1);

    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>The RMS of 16-bit samples above which a buffer counts as speech.</summary>
    private const double SpeechThreshold = 600;

    private int                    _running;      // 0 / 1, toggled atomically
    private WaveIn?                _waveIn;
    private ISpeechToTextSession?  _session;
    private Channel<byte[]>?       _audio;
    private Task?                  _pump, _reading;
    private CancellationTokenSource? _listening;
    private readonly List<string>  _finals = [];
    private string                 _partial = string.Empty;
    private bool                   _heardSpeech;
    private long                   _lastSpeechTick;

    public bool IsRecording => Volatile.Read(ref _running) == 1;

    public event EventHandler<bool>?   RecordingChanged;
    /// <summary>Everything heard so far: the finished utterances, then the one still being spoken.</summary>
    public event EventHandler<string>? TranscriptUpdated;
    public event EventHandler<string>? Failed;

    /// <summary>
    /// Starts listening at once: capture begins and the mic shows it before the model's session is open, and what is
    /// heard meanwhile waits in the queue for it — a slow start to a session costs no words.
    /// </summary>
    public async Task StartAsync(ISpeechToTextProvider provider)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            _finals.Clear();
            _partial        = string.Empty;
            _heardSpeech    = false;
            _lastSpeechTick = Environment.TickCount64;
            _listening      = new CancellationTokenSource();
            _audio          = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

            _waveIn = new WaveIn { WaveFormat = CaptureFormat, BufferMilliseconds = 100 };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            RecordingChanged?.Invoke(this, true);

            var session = await provider.StartSessionAsync(new SpeechToTextOptions(), _listening.Token);
            if (!IsRecording)
            {
                // Stopped while the session was opening: nothing is waiting for it.
                await session.DisposeAsync();
                return;
            }
            _session = session;
            _pump    = Task.Run(PumpAsync);
            _reading = Task.Run(ReadAsync);
            _ = Task.Run(WatchSilenceAsync);
        }
        catch (OperationCanceledException) when (!IsRecording)
        {
            // Stopped while the session was opening — StopAsync has already put everything away.
        }
        catch (Exception ex)
        {
            var wasShown = _waveIn is not null;
            await TearDownAsync();
            Interlocked.Exchange(ref _running, 0);
            if (wasShown) RecordingChanged?.Invoke(this, false);
            Failed?.Invoke(this, ex.Message);
        }
    }

    /// <summary>Stops listening and waits for the model's last word on what it heard.</summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _running, 0) == 0) return;

        if (_waveIn is { } waveIn)
        {
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waveIn.RecordingStopped += (_, _) => stopped.TrySetResult();
            try { waveIn.StopRecording(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* the device may already be gone */ }
        }

        try
        {
            _audio?.Writer.TryComplete();
            if (_pump is not null) await _pump;
            if (_session is not null) await _session.CompleteAsync();
            if (_reading is not null) await _reading;
        }
        catch (Exception ex) { Failed?.Invoke(this, ex.Message); }
        finally
        {
            await TearDownAsync();
            RecordingChanged?.Invoke(this, false);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _audio?.Writer.TryWrite(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());

        if (Rms(e.Buffer, e.BytesRecorded) > SpeechThreshold)
        {
            _heardSpeech    = true;
            _lastSpeechTick = Environment.TickCount64;
        }
    }

    /// <summary>Hands captured audio to the session in order, one buffer at a time.</summary>
    private async Task PumpAsync()
    {
        await foreach (var buffer in _audio!.Reader.ReadAllAsync())
            await _session!.AppendAudioAsync(buffer);
    }

    private async Task ReadAsync()
    {
        try
        {
            await foreach (var e in _session!.ReadEventsAsync(_listening!.Token))
            {
                switch (e)
                {
                    case TranscriptPartial p: _partial = p.Text; break;
                    case TranscriptFinal f:   _finals.Add(f.Text); _partial = string.Empty; break;
                    default: continue;
                }
                TranscriptUpdated?.Invoke(this, Transcript());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex.Message);
            _ = Task.Run(StopAsync);
        }
    }

    private string Transcript()
    {
        var text = new StringBuilder(string.Join(" ", _finals));
        if (_partial.Length > 0) text.Append(text.Length > 0 ? " " : "").Append(_partial);
        return text.ToString();
    }

    private async Task WatchSilenceAsync()
    {
        while (IsRecording)
        {
            await Task.Delay(200);
            if (_heardSpeech && Environment.TickCount64 - _lastSpeechTick > SilenceTimeout.TotalMilliseconds)
            {
                await StopAsync();
                return;
            }
        }
    }

    private async Task TearDownAsync()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;
        }
        if (_session is not null) { await _session.DisposeAsync(); _session = null; }
        _listening?.Cancel();
        _listening?.Dispose();
        _listening = null;
        _audio = null;
        _pump = _reading = null;
    }

    private static double Rms(byte[] buffer, int bytes)
    {
        var samples = bytes / 2;
        if (samples == 0) return 0;
        double sumSq = 0;
        for (var i = 0; i + 1 < bytes; i += 2)
        {
            var s = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSq += (double)s * s;
        }
        return Math.Sqrt(sumSq / samples);
    }
}
