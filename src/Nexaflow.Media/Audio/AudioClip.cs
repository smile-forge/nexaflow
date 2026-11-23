namespace Nexaflow.Media.Audio;

/// <summary>Interleaved float samples with their layout — a recording, or a WAV read for playback.</summary>
public sealed record AudioClip(float[] Samples, int Channels, int Rate)
{
    public TimeSpan Length => Channels == 0 || Rate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Samples.Length / Channels / Rate);
}
