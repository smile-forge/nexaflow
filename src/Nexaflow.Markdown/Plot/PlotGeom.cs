using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// What a plot draws its table as.
///
/// <para>
/// The four fences pick one of these and nothing else: a scatter and a bubble plot are both
/// <see cref="Point"/>, differing only in whether a column is mapped to size. That is what makes one
/// grammar enough for the four, and it is ggplot2's own division — the data is the data, and the geom
/// says what is made of it.
/// </para>
/// </summary>
public enum PlotGeom
{
    /// <summary>A mark per row, at its x and y. A bubble plot is this with a column mapped to size.</summary>
    Point,

    /// <summary>A filled cell per row, at its x and y, coloured by its fill. The heat map of a table that already holds one value per cell.</summary>
    Tile,

    /// <summary>The plane cut into rectangles, each coloured by how many rows fall in it. The heat map of raw points.</summary>
    Bin2d,

    /// <summary>The same, in hexagons, which tile the plane without the rectangular grid's corner artefacts.</summary>
    Hex,

    /// <summary>The contours of a 2D kernel density estimate over the rows.</summary>
    Density2d,

    /// <summary>
    /// A tile per pair of numeric columns, coloured by how strongly they move together. The marks are
    /// worked out rather than written, so the table is the observations and not the matrix.
    /// </summary>
    Corr,
}

/// <summary>How a value becomes a distance along an axis.</summary>
public enum PlotScale
{
    Linear,

    Log10,
    Log2,

    /// <summary>The natural logarithm.</summary>
    Ln,

    Sqrt,

    /// <summary>Linear, with the axis running the other way.</summary>
    Reverse,

    /// <summary>What <c>log</c> is short for, because nobody writing one means any other base.</summary>
    Log = Log10,
}

/// <summary>What a 2D density is drawn as.</summary>
public enum PlotContour
{
    /// <summary>A line at each level.</summary>
    Lines,

    /// <summary>The space between two levels filled, which is what a reader sees a density as.</summary>
    Bands,

    /// <summary>Every point of the grid coloured by its density, with no levels at all.</summary>
    Raster,
}

/// <summary>What is worked out from the rows and drawn over them.</summary>
public enum PlotFit
{
    None,

    /// <summary>Least squares — a straight line through the points.</summary>
    Lm,

    /// <summary>Locally weighted regression — a curve that follows them.</summary>
    Loess,
}

/// <summary>Which correlation coefficient <c>stats:</c> reports.</summary>
public enum PlotMethod
{
    Pearson,
    Spearman,
    Kendall,
}

/// <summary>Where the key goes, if anywhere.</summary>
public enum PlotLegend
{
    Right,
    Bottom,
    Left,
    Top,
    None,
}

/// <summary>Which gridlines are drawn behind the marks.</summary>
public enum PlotGrid
{
    Both,
    X,
    Y,
    None,
}

/// <summary>
/// A channel a column can be mapped to, which is ggplot2's <c>aes()</c>.
///
/// <para>
/// Every one of these is written under its own key, and the same key sets the channel for every mark
/// where it names no column — <c>size: pop</c> against <c>size: 4</c>. So a mapping and a constant need
/// no separate syntax, which is most of why the vocabulary stays small.
/// </para>
/// </summary>
public enum PlotAesthetic
{
    X,
    Y,
    Colour,
    Fill,
    Size,
    Shape,
    Alpha,
    Label,
    Group,

    /// <summary>Which panel a mark is drawn in, where the block splits the plot into several.</summary>
    Facet,
}

/// <summary>
/// Which of the four fences opened the block.
///
/// <para>
/// The fences differ only in what they ask for by default, and a block may say otherwise — so this is a
/// default and never a constraint. It is kept apart from <see cref="PlotGeom"/> because the fence
/// carries one thing the geom does not: which channel a third column feeds, and that is the whole
/// difference between a scatter plot and a bubble plot.
/// </para>
/// </summary>
public enum PlotFence
{
    Scatter,
    Bubble,
    Heatmap,
    Density2d,
}

/// <summary>What each fence asks for.</summary>
public static class PlotFences
{
    /// <summary>The geom the fence's name asks for, which a <c>geom:</c> line overrides.</summary>
    public static PlotGeom Geom(PlotFence fence) => fence switch
    {
        PlotFence.Heatmap => PlotGeom.Tile,
        PlotFence.Density2d => PlotGeom.Density2d,
        _ => PlotGeom.Point,
    };

    /// <summary>
    /// The channel a third column feeds where nobody mapped one — read off the geom first, because a
    /// block that asked for one is asking for what that geom needs, and off the fence otherwise.
    /// </summary>
    public static PlotAesthetic? Third(PlotFence fence, PlotGeom geom) => geom switch
    {
        PlotGeom.Tile => PlotAesthetic.Fill,
        PlotGeom.Point => fence == PlotFence.Bubble ? PlotAesthetic.Size : null,
        _ => null,
    };

    /// <summary>The fence a fenced block's language names, or null where it names none of them.</summary>
    public static PlotFence? Named(string? language) => SettingKeys.Plain(language) switch
    {
        "scatter" or "scatterplot" => PlotFence.Scatter,
        "bubble" or "bubbleplot" => PlotFence.Bubble,
        "heatmap" => PlotFence.Heatmap,
        "density2d" => PlotFence.Density2d,
        _ => null,
    };
}
