using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images;

/// <summary>
/// The Images settings every tab shares, in Options → Images: how much a layered picture's undo history may hold — counted
/// in the tiles of pixels, masks and selections steps took out of the picture, not in steps — before its oldest steps go;
/// whether the Layers panel shows; and how Paint's brush follows a pen and spaces itself along a stroke.
/// </summary>
public sealed class ImagesConfig : IFeatureConfig
{
    /// <summary>The history's budget unless set: a gigabyte.</summary>
    public const int DefaultHistoryMegabytes = 1024;

    public string ConfigName => "images";

    public string FriendlyName => Str.Get("Images.Config.Images");

    [ConfigDisplayName("Images.Config.HistoryMegabytes")]
    public int HistoryMegabytes { get; set; } = DefaultHistoryMegabytes;

    /// <summary>
    /// The layered pictures opened lately, newest first, for New picture to offer again — files and drafts not saved yet. Not an
    /// Options setting: written as canvases are opened, and kept so the list outlives the app.
    /// </summary>
    public List<string> RecentCanvases { get; set; } = [];

    /// <summary>The Artist panels' groups folded away — Light, Brush, Refine — by name, so every Artist tab opens its panels as
    /// they were left (<c>PanelFolds</c>). Not an Options setting: written as groups are folded and opened. To begin with, the
    /// Layers panel's mask, pixels and canvas sections, wanted less often than the layer list above them, and Adjust's Light,
    /// Colour and Depth, so the panel opens short, on the histogram and the quick fixes.</summary>
    public List<string> FoldedPanelGroups { get; set; } = ["LayersMask", "LayersPixels", "LayersCanvas", "AdjustLight", "AdjustColour", "AdjustDepth"];

    /// <summary>
    /// The colours kept on the palette beside the canvas, as colour specs — a swatch the theme tunes, or a colour of its own —
    /// in the order shown. Not an Options setting: written as colours are added and taken off. To begin with, the swatch bank's
    /// main hues.
    /// </summary>
    public List<string> Palette { get; set; } =
        ["swatch:Red", "swatch:Orange", "swatch:Yellow", "swatch:Green", "swatch:Teal", "swatch:Blue", "swatch:Purple", "swatch:Pink", "swatch:Slate"];

    /// <summary>Paint's spacing unless set: close enough that a stroke reads as one line, however light the pen.</summary>
    public const int DefaultBrushSpacing = 10;

    /// <summary>A layered picture's Layers panel shows beside the tools. Also switched from the Artist toolbar.</summary>
    [ConfigDisplayName("Images.Config.ShowsLayersPanel")]
    public bool ShowsLayersPanel { get; set; } = true;

    /// <summary>A pen pressed lightly paints a smaller brush.</summary>
    [ConfigDisplayName("Images.Config.PenPressureSize")]
    public bool PenPressureSize { get; set; } = true;

    /// <summary>A pen pressed lightly paints more faintly.</summary>
    [ConfigDisplayName("Images.Config.PenPressureOpacity")]
    public bool PenPressureOpacity { get; set; }

    /// <summary>How far apart Paint lays the brush down along a stroke, in percent of the brush as it is laid there.</summary>
    [ConfigDisplayName("Images.Config.BrushSpacing")]
    public int BrushSpacing { get; set; } = DefaultBrushSpacing;

    /// <summary>The budget in bytes, never less than enough for a step or two.</summary>
    public long HistoryBudget => Math.Max(64, HistoryMegabytes) * 1024L * 1024L;
}
