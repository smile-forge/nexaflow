using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// Owns the NAudio output device + decoder for one track and exposes a small transport surface
/// (load / play / pause / stop / seek / volume) plus the live spectrum bands. The signal chain is
/// <c>reader → SampleAggregator (FFT tap) → VolumeSampleProvider → WaveOut</c> so the visualiser
/// always sees the music regardless of the volume setting. Created lazily by the view-model (no device
/// is touched until the user actually plays something), and disposed when the tab closes.
/// </summary>
public sealed class AudioPlaybackEngine : IDisposable
{
    private IWavePlayer? _output;
    private WaveStream? _reader;
    private SampleAggregator? _aggregator;
    private VolumeSampleProvider? _volume;

    private float _targetVolume = 0.8f;
    private bool _suppressEnded;

    /// <summary>Raised on the output thread when a track reaches its natural end (drives auto-advance).</summary>
    public event Action? PlaybackEnded;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool HasMedia  => _reader is not null;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime  ?? TimeSpan.Zero;
    public float[] Bands => _aggregator?.LatestBands ?? [];

    public float Volume
    {
        get => _targetVolume;
        set
        {
            _targetVolume = Math.Clamp(value, 0f, 1f);
            if (_volume is not null) _volume.Volume = _targetVolume;
        }
    }

    /// <summary>Loads <paramref name="path"/> ready to play. Replaces any current track. Throws on decode failure.</summary>
    public void Load(string path, int bandCount)
    {
        Release();

        _reader = AudioReaderFactory.CreateReader(path);
        _aggregator = new SampleAggregator(_reader.ToSampleProvider()) { BandCount = bandCount };
        _volume = new VolumeSampleProvider(_aggregator) { Volume = _targetVolume };

        _output = new WaveOut();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(_volume);
    }

    public void Play()  { if (_output is { } o && o.PlaybackState != PlaybackState.Playing) o.Play(); }
    public void Pause() => _output?.Pause();

    public void Stop()
    {
        _suppressEnded = true;
        _output?.Stop();
        if (_reader is not null) _reader.CurrentTime = TimeSpan.Zero;
        _aggregator?.Reset();
    }

    /// <summary>Seeks the current track, clamped to its length. Safe to call while playing or paused.</summary>
    public void Seek(TimeSpan position)
    {
        if (_reader is null) return;
        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero
                    : position > _reader.TotalTime ? _reader.TotalTime
                    : position;
        _reader.CurrentTime = clamped;
    }

    /// <summary>Tears down the output device + decoder, releasing the file handle (needed before tag writes).</summary>
    public void Release()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try { _output.Stop(); } catch { /* device may already be gone */ }
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
        _aggregator = null;
        _volume = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Pause() does not raise this; only natural end, Stop(), or an error do. We suppress Stop()
        // ourselves, so an un-suppressed, error-free stop is a track that played to the end.
        if (_suppressEnded) { _suppressEnded = false; return; }
        if (e.Exception is not null) return;
        PlaybackEnded?.Invoke();
    }

    public void Dispose() => Release();
}
