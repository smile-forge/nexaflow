using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Xy;

/// <summary>What a series is drawn as.</summary>
public enum XySeriesKind
{
    Bar,
    Line,
}

/// <summary>One of an x-axis's categories: what it says as written, and the hole standing where it is still to write.</summary>
public sealed record XyCategory(ContentPart Name, ContentPart? Hole);

/// <summary>One axis, read: the line it was written on, its title, and its categories or its range.</summary>
/// <param name="Part">The axis line — what pressing the axis means.</param>
/// <param name="Title">What its title says, without its quotes, or null where it has none.</param>
/// <param name="Categories">Its categories in the order written — none for an axis of numbers.</param>
/// <param name="Min">Where its range starts, as written, or null.</param>
/// <param name="Max">Where its range ends, as written, or null.</param>
public sealed record XyAxis(ContentPart Part, ContentPart? Title, IReadOnlyList<XyCategory> Categories, double? Min, double? Max)
{
    /// <summary>The hole standing where its title is still to write, where holes were asked for and it is.</summary>
    public ContentPart? TitleHole { get; init; }

    public bool Categorical => Categories.Count > 0;

    /// <summary>Whether both ends of its range are written.</summary>
    public bool Ranged => Min is not null && Max is not null;
}

/// <summary>One value of a series: the number as written, what it comes to, and its label.</summary>
/// <param name="Part">The value and its label as written — what pressing its bar means.</param>
/// <param name="Value">The number as written — empty where it is still to come.</param>
/// <param name="Worth">What the number comes to, or null where it is none.</param>
/// <param name="Label">What its label says, without its quotes, or null where it has none.</param>
public sealed record XyPoint(ContentPart Part, ContentPart Value, double? Worth, ContentPart? Label)
{
    /// <summary>The hole standing where its label is still to write, where holes were asked for and it is.</summary>
    public ContentPart? LabelHole { get; init; }
}

/// <summary>One series, read: what it is drawn as, its name, its values, and its colour.</summary>
/// <param name="Part">The series line — what pressing its line means, and its legend row.</param>
/// <param name="Name">What its name says, without its quotes, or null where it has none — and then it has no legend row.</param>
/// <param name="Order">Where it comes among the series written, which is the colour it takes.</param>
/// <param name="Colour">The colour <c>plotColorPalette</c> writes for its place, or null to leave it to the theme.</param>
public sealed record XySeries(ContentPart Part, XySeriesKind Kind, ContentPart? Name, IReadOnlyList<XyPoint> Points, int Order, string? Colour)
{
    /// <summary>The hole standing where its name is still to write, where holes were asked for and it is.</summary>
    public ContentPart? NameHole { get; init; }
}

/// <summary>
/// An <c>xychart</c> block, read: which way it runs, its two axes, its series in the order they are written, and what its
/// front matter asks for. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// Each series' values stand over the categories in order — the first over the first — and an axis written twice is the last
/// one written. Where the y-axis writes no range, the chart's is the values' own, from nought where there are bars.
/// </para>
/// </summary>
public sealed class XyChart
{
    private XyChart(MermaidBlock block, XyConfig config, XyOrientation orientation, XyAxis? x, XyAxis? y, IReadOnlyList<XySeries> series)
    {
        Block = block;
        Config = config;
        Orientation = orientation;
        X = x;
        Y = y;
        Series = series;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static XyChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>Reads a block that has already been read.</summary>
    public static XyChart Of(MermaidBlock block)
    {
        var config = XyConfig.Read(block.Config);
        var orientation = XyOrientation.Vertical;
        XyAxis? x = null, y = null;
        var series = new List<XySeries>();

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case XyKinds.Orientation when part.Text.Equals(XyGrammar.Horizontal, StringComparison.OrdinalIgnoreCase):
                    orientation = XyOrientation.Horizontal;
                    break;

                case XyKinds.Axis when Word(part) is { } word:
                    if (word.Equals(XyGrammar.XAxis, StringComparison.OrdinalIgnoreCase)) x = Axis(part);
                    else y = Axis(part);
                    break;

                case XyKinds.Series when Word(part) is { } word:
                    var order = series.Count;
                    series.Add(Plotted(part, word, order, config.Palette.Count > 0 ? config.Palette[order % config.Palette.Count] : null));
                    break;
            }
        }

        return new XyChart(block, config, config.Orientation ?? orientation, x, y, series);
    }

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    public XyConfig Config { get; }

    public XyOrientation Orientation { get; }

    /// <summary>The x-axis — the categories, or the numbers the values stand over — or null where none is written.</summary>
    public XyAxis? X { get; }

    /// <summary>The y-axis — the numbers the values reach — or null where none is written.</summary>
    public XyAxis? Y { get; }

    /// <summary>The series, in the order written: bars side by side in that order, and lines over the bars.</summary>
    public IReadOnlyList<XySeries> Series { get; }

    /// <summary>How many places along the x-axis the values stand at: its categories, or else the most values any series has.</summary>
    public int Slots => X is { Categorical: true } x ? x.Categories.Count : Series.Select(each => each.Points.Count).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The numbers the values are drawn against: the y-axis's range where both its ends are written, and otherwise the values'
    /// own — from nought where there are bars, and one wide at the least.
    /// </summary>
    public (double Min, double Max) Range
    {
        get
        {
            if (Y is { Ranged: true } y && y.Max > y.Min) return (y.Min!.Value, y.Max!.Value);

            var values = Series.SelectMany(each => each.Points).Select(point => point.Worth).OfType<double>().ToList();
            if (values.Count == 0) return (0, 1);

            var min = values.Min();
            var max = values.Max();
            if (Series.Any(each => each.Kind == XySeriesKind.Bar)) (min, max) = (Math.Min(min, 0), Math.Max(max, 0));

            return max > min ? (min, max) : (min, min + 1);
        }
    }

    /// <summary>What an axis or series line starts with — <c>x-axis</c>, <c>bar</c> — as written.</summary>
    private static string? Word(ContentPart part) => part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text;

    /// <summary>A title or a name: in quotes, or a word.</summary>
    private static ContentPart? Titled(ContentPart part) =>
        part.Children.FirstOrDefault(child => child.Kind is MermaidKinds.Quoted or MermaidKinds.Name);

    private static XyAxis Axis(ContentPart part)
    {
        var title = Titled(part);
        var categories = part.Children.FirstOrDefault(child => child.Kind == XyKinds.Categories)?.Children
                             .FirstOrDefault(child => child.Kind == MermaidKinds.Names)
                             .Named()
                             .Select(name => new XyCategory(name.Words()!, name.Hole()))
                             .ToList() ?? [];

        var ends = part.Children.FirstOrDefault(child => child.Kind == XyKinds.Range)?.Children
                       .Where(child => child.Kind == MermaidKinds.Amount)
                       .ToList() ?? [];

        return new XyAxis(part, title.Words(), categories, ends.ElementAtOrDefault(0).Number(), ends.ElementAtOrDefault(1).Number())
        {
            TitleHole = title.Hole(),
        };
    }

    private static XySeries Plotted(ContentPart part, string word, int order, string? colour)
    {
        var name = Titled(part);
        var points = part.Children.FirstOrDefault(child => child.Kind == XyKinds.Values)?.Children
                         .Where(child => child.Kind == XyKinds.Point)
                         .Select(point =>
                         {
                             var label = point.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Quoted);
                             return new XyPoint(point, point.Inner(MermaidKinds.Number)!, point.Number(), label.Words()) { LabelHole = label.Hole() };
                         })
                         .ToList() ?? [];

        var kind = word.Equals(XyGrammar.Line, StringComparison.OrdinalIgnoreCase) ? XySeriesKind.Line : XySeriesKind.Bar;
        return new XySeries(part, kind, name.Words(), points, order, colour) { NameHole = name.Hole() };
    }
}
