namespace Nexaflow.IO.Common.Audio;

/// <summary>
/// A test signal, generated as it plays: whatever channel count and rate the endpoint mixes at, written straight into
/// its buffer as interleaved float.
/// </summary>
public interface IAudioSignal
{
    /// <summary>Fills up to <c>interleaved.Length / channels</c> frames and returns how many it wrote; fewer than asked
    /// means the signal has ended.</summary>
    int Read(Span<float> interleaved, int channels, int rate);
}

/// <summary>Levels and their arithmetic: dBFS, where 0 is the loudest a sample can be.</summary>
public static class AudioLevels
{
    /// <summary>The quietest level this reports; silence reads as this rather than minus infinity.</summary>
    public const float Floor = -96f;

    public static float Amplitude(double decibels) => (float)Math.Pow(10, decibels / 20);

    public static float Decibels(float amplitude) => amplitude <= 0 ? Floor : Math.Max(Floor, 20f * MathF.Log10(amplitude));

    /// <summary>Each channel's peak and RMS over a block of interleaved frames, in dBFS.</summary>
    public static void Measure(ReadOnlySpan<float> interleaved, int channels, Span<float> peakDb, Span<float> rmsDb)
    {
        int frames = interleaved.Length / channels;
        for (int c = 0; c < channels; c++)
        {
            float peak = 0;
            double sum = 0;
            for (int f = 0; f < frames; f++)
            {
                float s = interleaved[f * channels + c];
                float a = Math.Abs(s);
                if (a > peak) peak = a;
                sum += s * (double)s;
            }
            peakDb[c] = Decibels(peak);
            rmsDb[c] = frames == 0 ? Floor : Decibels((float)Math.Sqrt(sum / frames));
        }
    }
}

/// <summary>Which channels a signal plays on: all, or a chosen few by index.</summary>
public sealed class ChannelSelection
{
    readonly HashSet<int>? _only;

    ChannelSelection(HashSet<int>? only) => _only = only;

    public static ChannelSelection All { get; } = new(null);

    public static ChannelSelection Only(params int[] channels) => new([.. channels]);

    public bool Includes(int channel) => _only is null || _only.Contains(channel);
}

/// <summary>A sine at one frequency and level, forever or for <paramref name="duration"/>.</summary>
public sealed class ToneSignal(double frequency, double level, ChannelSelection selection, TimeSpan? duration = null) : IAudioSignal
{
    double _phase;
    long _frames;

    public int Read(Span<float> interleaved, int channels, int rate)
    {
        int frames = interleaved.Length / channels;
        if (duration is { } d) frames = (int)Math.Min(frames, Math.Max(0, (long)(d.TotalSeconds * rate) - _frames));
        double step = 2 * Math.PI * frequency / rate;
        for (int f = 0; f < frames; f++)
        {
            float s = (float)(level * Math.Sin(_phase));
            _phase += step;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            for (int c = 0; c < channels; c++) interleaved[f * channels + c] = selection.Includes(c) ? s : 0;
        }
        _frames += frames;
        return frames;
    }
}

/// <summary>
/// A logarithmic sweep from <paramref name="from"/> to <paramref name="to"/> Hz: equal time per octave, which is how
/// an ear (and a frequency-response measurement) hears it.
/// </summary>
public sealed class SweepSignal(double from, double to, TimeSpan duration, double level, ChannelSelection selection) : IAudioSignal
{
    double _phase;
    long _frames;

    /// <summary>The frequency playing now.</summary>
    public double Frequency { get; private set; } = from;

    public int Read(Span<float> interleaved, int channels, int rate)
    {
        long total = (long)(duration.TotalSeconds * rate);
        int frames = (int)Math.Min(interleaved.Length / channels, Math.Max(0, total - _frames));
        double ratio = Math.Log(to / from);
        for (int f = 0; f < frames; f++)
        {
            double t = (double)(_frames + f) / total;
            Frequency = from * Math.Exp(ratio * t);
            _phase += 2 * Math.PI * Frequency / rate;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            float s = (float)(level * Math.Sin(_phase));
            for (int c = 0; c < channels; c++) interleaved[f * channels + c] = selection.Includes(c) ? s : 0;
        }
        _frames += frames;
        return frames;
    }
}

/// <summary>
/// A recording played back: its channels laid onto the endpoint's (a mono clip on every channel, a stereo one on the
/// first two), resampled linearly to the endpoint's rate, at <paramref name="level"/>.
/// </summary>
public sealed class ClipSignal(float[] samples, int clipChannels, int clipRate, double level) : IAudioSignal
{
    double _position;

    public TimeSpan Length => TimeSpan.FromSeconds((double)samples.Length / clipChannels / clipRate);

    public int Read(Span<float> interleaved, int channels, int rate)
    {
        int clipFrames = samples.Length / clipChannels;
        double step = (double)clipRate / rate;
        int frames = interleaved.Length / channels, f = 0;
        for (; f < frames && _position < clipFrames - 1; f++, _position += step)
        {
            int at = (int)_position;
            float frac = (float)(_position - at);
            for (int c = 0; c < channels; c++)
            {
                int source = clipChannels == 1 ? 0 : c;
                if (source >= clipChannels) { interleaved[f * channels + c] = 0; continue; }
                float a = samples[at * clipChannels + source], b = samples[(at + 1) * clipChannels + source];
                interleaved[f * channels + c] = (float)(level * (a + (b - a) * frac));
            }
        }
        return f;
    }
}

/// <summary>
/// Each channel in turn: a burst of tone on one channel, a pause, the next. <see cref="CurrentChannel"/> says which is
/// sounding, so the page can name it while it plays — how a person finds out which speaker is "Side left".
/// </summary>
public sealed class ChannelWalkSignal(double frequency, double level, TimeSpan burst, TimeSpan gap) : IAudioSignal
{
    long _frames;
    double _phase;

    /// <summary>The channel sounding now, or -1 between bursts and once the walk is over.</summary>
    public int CurrentChannel { get; private set; } = -1;

    public int Read(Span<float> interleaved, int channels, int rate)
    {
        long burstFrames = (long)(burst.TotalSeconds * rate), slot = burstFrames + (long)(gap.TotalSeconds * rate);
        long total = slot * channels;
        int frames = (int)Math.Min(interleaved.Length / channels, Math.Max(0, total - _frames));
        double step = 2 * Math.PI * frequency / rate;
        for (int f = 0; f < frames; f++)
        {
            long at = _frames + f;
            int channel = (int)(at / slot);
            bool sounding = at % slot < burstFrames;
            // A short fade at each end of a burst, so switching channels does not click.
            long into = at % slot;
            double envelope = sounding ? Math.Min(1, Math.Min(into, burstFrames - into) / (rate * 0.01)) : 0;
            float s = (float)(level * envelope * Math.Sin(_phase));
            _phase += step;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            for (int c = 0; c < channels; c++) interleaved[f * channels + c] = c == channel ? s : 0;
            CurrentChannel = sounding ? channel : -1;
        }
        _frames += frames;
        if (frames == 0) CurrentChannel = -1;
        return frames;
    }
}
