using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Audio;

/// <summary>
/// Global audio-player settings (one instance per process, persisted as versioned JSON). Surfaced
/// in the Options panel via its public properties.
/// </summary>
public sealed class AudioConfig : IFeatureConfig
{
    public string ConfigName   => "audio";
    public string FriendlyName => Str.Get("Audio.Config.Audio");

    /// <summary>Last-used playback volume (0..1), restored on the next track.</summary>
    [ConfigDisplayName("Audio.Config.Audio.Volume")]
    public double Volume { get; set; } = 0.8;

    /// <summary>When true, finishing a track advances to the next item in the queue.</summary>
    [ConfigDisplayName("Audio.Config.Audio.AutoAdvance")]
    public bool AutoAdvance { get; set; } = true;

    /// <summary>Number of bars drawn by the spectrum analyser.</summary>
    [ConfigDisplayName("Audio.Config.Audio.SpectrumBarCount")]
    public int SpectrumBarCount { get; set; } = 64;

    /// <summary>When true, leaving the audio tab hands playback to a transport control in the shell chrome
    /// (playback keeps running) instead of pausing; the page retakes over when it becomes active again.</summary>
    [ConfigDisplayName("Audio.Config.Audio.BackgroundPlay")]
    public bool BackgroundPlay { get; set; }
}
