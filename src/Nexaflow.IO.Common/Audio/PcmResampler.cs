namespace Nexaflow.IO.Common.Audio;

/// <summary>
/// Converts a stream of 16-bit mono PCM from one sample rate to another by linear interpolation, chunk by chunk:
/// the position between chunks is carried over, so a stream cut anywhere comes out as if it had been converted
/// whole. Good enough for speech going to a recogniser or a model; not for music.
/// </summary>
public sealed class PcmResampler(int fromRate, int toRate)
{
    /// <summary>Where the next output sample falls, in input samples from the start of the next chunk, counted in
    /// 1/<c>toRate</c> steps so it never drifts; it may be negative, between the last sample of the previous chunk
    /// and the first of the next.</summary>
    private long  _position;
    private short _previous;
    private bool  _started;

    public byte[] Convert(ReadOnlySpan<byte> pcm)
    {
        var count = pcm.Length / 2;
        if (count == 0) return [];

        var output = new List<byte>((int)((long)count * toRate / fromRate) * 2 + 4);
        if (!_started) { _previous = Sample(pcm, 0); _started = true; }

        var last = (long)(count - 1) * toRate;
        while (_position < last)
        {
            var index = (int)Math.Floor((double)_position / toRate);
            var frac  = (double)(_position - (long)index * toRate) / toRate;
            var a     = index < 0 ? _previous : Sample(pcm, index);
            var b     = Sample(pcm, index + 1);
            var value = (short)Math.Round(a + (b - a) * frac);

            output.Add((byte)value);
            output.Add((byte)(value >> 8));
            _position += fromRate;
        }

        _previous  = Sample(pcm, count - 1);
        _position -= (long)count * toRate;
        return [.. output];
    }

    private static short Sample(ReadOnlySpan<byte> pcm, int index) => (short)(pcm[index * 2] | (pcm[index * 2 + 1] << 8));
}
