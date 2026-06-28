using Nexaflow.Features.Common;

namespace Nexaflow.Features.Video;

/// <summary>
/// Global settings for the Video player. Hardware-accelerated decoding is off by default because the
/// libvlc 4 WPF render path is steadier in software on many machines; users whose hardware handles it
/// (e.g. for smooth 4K) can opt in. The setting takes effect for newly opened videos.
/// </summary>
public sealed class VideoConfig : IFeatureConfig
{
    public string ConfigName   => "video";
    public string FriendlyName => "Video";

    [ConfigDisplayName("Use hardware-accelerated decoding (faster; turn off if playback is unstable)")]
    public bool EnableHardwareDecoding { get; set; }
}
