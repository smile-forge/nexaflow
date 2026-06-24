using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Compressed;

/// <summary>Global settings for the Compressed feature — currently the default format the "Zip It"
/// folder action uses unless the user picks another in its overlay.</summary>
public sealed class CompressedConfig : IFeatureConfig
{
    public string ConfigName => "compressed";
    public string FriendlyName => "Compressed";

    [ConfigDisplayName("Default \"Zip It\" format")]
    [ListSource(typeof(CompressedConfig), nameof(GetFormatOptions))]
    public string DefaultFormat { get; set; } = "zip";

    public static IEnumerable<string> GetFormatOptions() => ["zip", "tar", "tar.gz", "7z", "zst"];
}
