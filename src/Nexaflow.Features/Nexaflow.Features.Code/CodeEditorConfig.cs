using Nexaflow.Features.Common;

namespace Nexaflow.Features.Code;

/// <summary>
/// Global settings for the "As Code" editor. The editor loads the whole file into memory, so files larger
/// than this ceiling open read-only (with a prompt to view them As Text or split them).
/// </summary>
public sealed class CodeEditorConfig : IFeatureConfig
{
    public string ConfigName   => "code";
    public string FriendlyName => "Code Editor";

    [ConfigDisplayName("Max editable file size")]
    [ListSource(typeof(CodeEditorConfig), nameof(GetSizeOptions))]
    public string MaxEditableFileSize { get; set; } = "50 MB";

    public static IEnumerable<string> GetSizeOptions() =>
        ["5 MB", "10 MB", "25 MB", "50 MB", "100 MB", "250 MB"];

    /// <summary>The configured ceiling in bytes; files larger than this open read-only.</summary>
    public long GetMaxEditableBytes() => MaxEditableFileSize switch
    {
        "5 MB"   => 5L   * 1024 * 1024,
        "10 MB"  => 10L  * 1024 * 1024,
        "25 MB"  => 25L  * 1024 * 1024,
        "100 MB" => 100L * 1024 * 1024,
        "250 MB" => 250L * 1024 * 1024,
        _        => 50L  * 1024 * 1024,
    };
}
