using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Code;

/// <summary>
/// Global settings for the "As Code" editor. The editor loads the whole file into memory, so files larger
/// than this ceiling open read-only (with a prompt to view them As Text or split them).
/// </summary>
public sealed class CodeEditorConfig : IFeatureConfig
{
    public string ConfigName   => "code";
    public string FriendlyName => Str.Get("Code.Config.CodeEditor");

    [ConfigDisplayName("Code.Config.CodeEditor.MaxEditableFileSize")]
    [ListSource(typeof(CodeEditorConfig), nameof(GetSizeOptions))]
    public string MaxEditableFileSize { get; set; } = "50 MB";

    public static IEnumerable<ConfigListOption> GetSizeOptions() =>
        new[] { 5, 10, 25, 50, 100, 250 }
            .Select(mb => new ConfigListOption($"{mb} MB", Str.Format("Code.Config.CodeEditor.Megabytes", mb)));

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
