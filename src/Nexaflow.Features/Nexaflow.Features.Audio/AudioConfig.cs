using Nexaflow.Features.Common;

namespace Nexaflow.Features.Audio;

/// <summary>
/// Global audio-player settings (one instance per process, persisted as versioned JSON). Surfaced
/// in the Options panel via its public properties.
/// </summary>
public sealed class AudioConfig : IFeatureConfig
{
    public string ConfigName   => "audio";
    public string FriendlyName => "Audio Player";

    /// <summary>Last-used playback volume (0..1), restored on the next track.</summary>
    public double Volume { get; set; } = 0.8;

    /// <summary>When true, finishing a track advances to the next item in the queue.</summary>
    public bool AutoAdvance { get; set; } = true;

    /// <summary>Number of bars drawn by the spectrum analyser.</summary>
    public int SpectrumBarCount { get; set; } = 64;

    /// <summary>When true, leaving the audio tab hands playback to a transport control in the shell chrome
    /// (playback keeps running) instead of pausing; the page retakes over when it becomes active again.</summary>
    public bool BackgroundPlay { get; set; }
}
