using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Video;

/// <summary>
/// Global settings for the Video player. Hardware-accelerated decoding is off by default because the
/// libvlc 4 WPF render path is steadier in software on many machines; users whose hardware handles it
/// (e.g. for smooth 4K) can opt in. The setting takes effect for newly opened videos.
/// </summary>
public sealed class VideoConfig : IFeatureConfig
{
    public string ConfigName   => "video";
    public string FriendlyName => Str.Get("Video.Config.Video");

    [ConfigDisplayName("Video.Config.Video.EnableHardwareDecoding")]
    public bool EnableHardwareDecoding { get; set; }
}
