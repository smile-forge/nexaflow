using System.IO;
using System.Text;
using System.Windows;
using NAudio.Wave;
using Whisper.net;

namespace Nexaflow.Core.Services;

/// <summary>
/// Owns microphone capture (NAudio) and speech-to-text (Whisper.net). Voice mode
/// is continuous: while listening it re-transcribes the growing buffer every second
/// and pushes the result via <see cref="TranscriptionUpdated"/> (so it owns the
/// input display and self-corrects as more audio arrives), and auto-stops after a
/// short silence once speech has been heard. Captured audio is 16 kHz mono PCM.
/// Recording is tracked with <see cref="RecordingChanged"/> only — it never flips
/// the AI-busy flag, so the listening UI and the processing UI stay distinct.
/// </summary>
public sealed class VoiceManager : IDisposable
{
    public static VoiceManager Instance { get; } = new();
    private VoiceManager() { }

    private static readonly WaveFormat CaptureFormat = new(16000, 16, 1);

    // Tuning: re-transcribe cadence, silence to auto-stop, speech amplitude floor.
    private const int    TranscribeIntervalMs = 1000;
    private const int    SilenceTimeoutMs     = 1500;
    private const double SpeechThreshold      = 600;   // RMS of int16 samples
    private const int    MinPcmBytes          = 16000 * 2 / 3;   // ~0.33 s before first pass

    private readonly SemaphoreSlim _transcribeLock = new(1, 1);
    private readonly Lock          _bufLock        = new();

    private WaveInEvent?    _waveIn;
    private MemoryStream?   _buffer;
    private WhisperFactory? _factory;
    private string?         _factoryModelPath;

    private int  _running;          // 0 / 1, toggled atomically
    private bool _heardVoice;
    private long _lastVoiceTick;

    public bool IsRecording => Volatile.Read(ref _running) == 1;

    /// <summary>Raised on the UI thread when listening starts/stops.</summary>
    public event EventHandler<bool>?   RecordingChanged;
    /// <summary>Raised on the UI thread with the latest (partial or final) transcription.</summary>
    public event EventHandler<string>? TranscriptionUpdated;
    /// <summary>Raised on the UI thread on capture/transcription failure.</summary>
    public event EventHandler<string>? Error;

    // ── Listening lifecycle ──────────────────────────────────────────────────

    public void StartListening()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            _buffer        = new MemoryStream();
            _heardVoice    = false;
            _lastVoiceTick = Environment.TickCount64;
            _waveIn        = new WaveInEvent { WaveFormat = CaptureFormat };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();

            Dispatch(() => RecordingChanged?.Invoke(this, true));
            _ = Task.Run(ListenLoopAsync);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _running, 0);
            CleanupCapture();
            Dispatch(() => Error?.Invoke(this, ex.Message));
        }
    }

    /// <summary>Stops listening (manual or auto), runs a final transcription.</summary>
    public async Task StopListeningAsync()
    {
        if (Interlocked.Exchange(ref _running, 0) == 0) return;

        if (_waveIn is not null)
        {
            var stopped = new TaskCompletionSource();
            _waveIn.RecordingStopped += (_, _) => stopped.TrySetResult();
            try { _waveIn.StopRecording(); await stopped.Task; } catch { /* ignore */ }
        }

        byte[] pcm;
        lock (_bufLock) pcm = _buffer?.ToArray() ?? [];
        CleanupCapture();

        Dispatch(() => RecordingChanged?.Invoke(this, false));

        if (pcm.Length == 0) return;
        await _transcribeLock.WaitAsync();
        try
        {
            string text = await Task.Run(() => TranscribeAsync(pcm));
            if (!string.IsNullOrWhiteSpace(text))
                Dispatch(() => TranscriptionUpdated?.Invoke(this, text));
        }
        catch (Exception ex) { Dispatch(() => Error?.Invoke(this, ex.Message)); }
        finally { _transcribeLock.Release(); }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_bufLock) _buffer?.Write(e.Buffer, 0, e.BytesRecorded);

        if (Rms(e.Buffer, e.BytesRecorded) > SpeechThreshold)
        {
            _heardVoice    = true;
            _lastVoiceTick = Environment.TickCount64;
        }
    }

    private async Task ListenLoopAsync()
    {
        while (Volatile.Read(ref _running) == 1)
        {
            await Task.Delay(TranscribeIntervalMs);
            if (Volatile.Read(ref _running) == 0) break;

            await TranscribePartialAsync();

            // Auto-stop once we've heard speech followed by a brief silence.
            if (_heardVoice && Environment.TickCount64 - _lastVoiceTick > SilenceTimeoutMs)
            {
                await StopListeningAsync();
                break;
            }
        }
    }

    private async Task TranscribePartialAsync()
    {
        if (!await _transcribeLock.WaitAsync(0)) return;   // skip if a pass is in flight
        try
        {
            byte[] pcm;
            lock (_bufLock) pcm = _buffer?.ToArray() ?? [];
            if (pcm.Length < MinPcmBytes) return;

            string text = await Task.Run(() => TranscribeAsync(pcm));
            if (Volatile.Read(ref _running) == 1 && !string.IsNullOrWhiteSpace(text))
                Dispatch(() => TranscriptionUpdated?.Invoke(this, text));
        }
        catch { /* transient partial failure — keep listening */ }
        finally { _transcribeLock.Release(); }
    }

    // ── Transcription ────────────────────────────────────────────────────────

    private async Task<string> TranscribeAsync(byte[] pcm)
    {
        var cfg = ConfigManager.Instance.GetAll().OfType<VoiceConfig>().FirstOrDefault()
                  ?? new VoiceConfig();
        var modelPath = WhisperModelManager.Instance.GetModelPath(cfg);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Voice model not downloaded.", modelPath);

        EnsureFactory(modelPath);

        using var processor = _factory!.CreateBuilder()
            .WithLanguage(cfg.Language == WhisperLanguage.EnglishOnly ? "en" : "auto")
            .Build();

        using var wav = BuildWavStream(pcm);
        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(wav))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    private void EnsureFactory(string modelPath)
    {
        if (_factoryModelPath == modelPath && _factory is not null) return;
        _factory?.Dispose();
        _factory          = WhisperFactory.FromPath(modelPath);
        _factoryModelPath = modelPath;
    }

    private static MemoryStream BuildWavStream(byte[] pcm)
    {
        var ms  = new MemoryStream();
        var raw = new RawSourceWaveStream(new MemoryStream(pcm), CaptureFormat);
        WaveFileWriter.WriteWavFileToStream(ms, raw);
        ms.Position = 0;
        return ms;
    }

    private static double Rms(byte[] buffer, int bytes)
    {
        int samples = bytes / 2;
        if (samples == 0) return 0;
        double sumSq = 0;
        for (int i = 0; i + 1 < bytes; i += 2)
        {
            short s = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSq += (double)s * s;
        }
        return Math.Sqrt(sumSq / samples);
    }

    private void CleanupCapture()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;
        }
        _buffer?.Dispose();
        _buffer = null;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _running, 0);
        CleanupCapture();
        _factory?.Dispose();
        _factory = null;
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}
