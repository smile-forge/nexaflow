namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Sample-set sub-directory → the AutomationId of the viewer its files should default-open in.
/// One map, two enforcement points: the per-file UI smoke (<see cref="SampleFileViewerTests"/>)
/// and the filemap completeness guard (<c>Architecture/FeatureTouchPointTests</c>). When you add
/// a viewer feature, add its row here (plus the sample set and the default-filemap.json entries).
/// </summary>
internal static class ViewerMap
{
    public static readonly (string SubDir, string ViewerId)[] BySet =
    [
        ("markdown", "MarkdownView"),
        ("tabular",  "TabularView"),
        ("text",     "TextView"),
        ("json",     "JsonView"),
        ("logs",     "LogView"),
        ("binary",   "HexView"),
        ("images",   "ImageView"),
        ("model3d",  "Model3DView"),
        ("audio",    "AudioView"),
        ("video",    "VideoView"),
        ("font",     "FontView"),
        ("svg",      "SvgView"),
    ];
}
