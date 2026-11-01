using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// The selection: what every tool that works on part of a picture works on — its coverage, a byte a pixel in shared
/// tiles, the bounds round what it covers, and the outline marching ants walk, traced once from the coverage the first
/// time it is asked for. An empty selection selects everything to every tool.
/// </summary>
public sealed class RegionSelection
{
    private IReadOnlyList<SKPoint[]>? _outline;

    public RegionSelection(TiledCoverage coverage) => Coverage = coverage;

    public TiledCoverage Coverage { get; }

    public int Width => Coverage.Width;

    public int Height => Coverage.Height;

    /// <summary>Nothing is selected — which to a tool is everything.</summary>
    public bool IsEmpty => Coverage.IsEmpty;

    /// <summary>The smallest rectangle round what is selected at all; empty where nothing is.</summary>
    public SKRectI Bounds => Coverage.Bounds;

    /// <summary>The loops round what is half selected or more, in the coverage's pixels.</summary>
    public IReadOnlyList<SKPoint[]> Outline => _outline ??= CoverageOutline.Trace(Coverage);

    /// <summary>The selection <paramref name="region"/> makes of <paramref name="start"/>, its lengths times
    /// <paramref name="scale"/>.</summary>
    public static RegionSelection Of(TiledCoverage start, RecipeRegion? region, double scale) => new(RegionRaster.Apply(start, region, scale));
}
