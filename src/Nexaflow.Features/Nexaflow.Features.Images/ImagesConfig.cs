using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images;

/// <summary>
/// The Images settings every tab shares, in Options → Images: how much a layered picture's undo history may hold — counted
/// in the tiles of pixels, masks and selections steps took out of the picture, not in steps — before its oldest steps go.
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

    /// <summary>The budget in bytes, never less than enough for a step or two.</summary>
    public long HistoryBudget => Math.Max(64, HistoryMegabytes) * 1024L * 1024L;
}
