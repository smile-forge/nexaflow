using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Quadrant;

/// <summary>Text somebody wrote: what it says, without its quotes, and the hole standing where it is still to write.</summary>
/// <param name="Part">The line it was written on — what pressing it means.</param>
public sealed record QuadrantText(ContentPart Part, ContentPart Says, ContentPart? Hole);

/// <summary>How a point is drawn, as its class and its own style write it — null for what neither writes.</summary>
public sealed record QuadrantStyle(double? Radius, string? Colour, string? StrokeColour, double? StrokeWidth)
{
    public static QuadrantStyle None { get; } = new(null, null, null, null);

    /// <summary>This style with what <paramref name="over"/> writes laid over it.</summary>
    public QuadrantStyle With(QuadrantStyle over) =>
        new(over.Radius ?? Radius, over.Colour ?? Colour, over.StrokeColour ?? StrokeColour, over.StrokeWidth ?? StrokeWidth);

    /// <summary>What a style's properties write, where none of them is wrong.</summary>
    internal static QuadrantStyle Of(ContentPart? properties)
    {
        var style = None;
        foreach (var property in properties?.Children.Where(child => child.Kind == MermaidKinds.Property) ?? [])
        {
            if (property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key) is not { } key
                || property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting) is not { Trouble: null, Length: > 0 } value)
                continue;

            style = key.Text.ToLowerInvariant() switch
            {
                "radius" => style with { Radius = MermaidNumber.Pixels(value.Text) is > 0 and var radius ? radius : style.Radius },
                "color" => style with { Colour = value.Text },
                "stroke-color" => style with { StrokeColour = value.Text },
                "stroke-width" => style with { StrokeWidth = MermaidNumber.Pixels(value.Text) },
                _ => style,
            };
        }

        return style;
    }
}

/// <summary>One point: its name, where it stands, and how it is drawn.</summary>
/// <param name="Part">The point as written — what pressing it means.</param>
/// <param name="X">How far across it stands, from 0 to 1 — or null where that is not written, or wrong.</param>
/// <param name="Y">How far up it stands, from 0 to 1.</param>
public sealed record QuadrantPoint(ContentPart Part, QuadrantText Name, double? X, double? Y, QuadrantStyle Style, int Order)
{
    public bool Placed => X is not null && Y is not null;
}

/// <summary>
/// A <c>quadrantChart</c> block, read: the ends of its axes, its quadrants' captions, and its points with their classes laid
/// under their own styles. Its title is the block's (<see cref="MermaidBlock.Title"/>). A line written twice is the last one written.
/// </summary>
public sealed class QuadrantChart
{
    private QuadrantChart(MermaidBlock block, QuadrantConfig config) => (Block, Config) = (block, config);

    public static QuadrantChart Read(string? block) => Of(MermaidParser.Read(block));

    public static QuadrantChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static QuadrantChart Of(MermaidBlock block)
    {
        var chart = new QuadrantChart(block, QuadrantConfig.Read(block.Config));
        var root = block.Reading.Root;

        var classes = new Dictionary<string, QuadrantStyle>(StringComparer.Ordinal);
        foreach (var line in root.SelfAndDescendants().Where(part => part.Kind == QuadrantKinds.Class))
            if (Name(line) is { Length: > 0 } name)
                classes[name] = QuadrantStyle.Of(Properties(line));

        var regions = new QuadrantText?[4];
        var points = new List<QuadrantPoint>();

        foreach (var part in root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case QuadrantKinds.Axis when Word(part) is { } word:
                    var ends = (Text(part, QuadrantRoles.Low), Text(part, QuadrantRoles.High));
                    if (word.Equals(QuadrantGrammar.XAxis, StringComparison.OrdinalIgnoreCase)) (chart.Left, chart.Right) = ends;
                    else (chart.Bottom, chart.Top) = ends;
                    break;

                case QuadrantKinds.Region when Word(part) is { } word:
                    var index = QuadrantGrammar.Regions.ToList().FindIndex(region => region.Equals(word, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) regions[index] = Text(part, QuadrantRoles.Caption);
                    break;

                case QuadrantKinds.Point when Text(part, QuadrantRoles.Name) is { } named:
                    var position = part.Children.FirstOrDefault(child => child.Kind == QuadrantKinds.Position);
                    var amounts = position?.Children.Where(child => child.Kind == MermaidKinds.Amount).ToList() ?? [];
                    var style = (Name(part) is { } taken && classes.TryGetValue(taken, out var inherited) ? inherited : QuadrantStyle.None)
                        .With(QuadrantStyle.Of(Properties(part)));

                    points.Add(new QuadrantPoint(part, named, amounts.ElementAtOrDefault(0).Number(), amounts.ElementAtOrDefault(1).Number(), style, points.Count));
                    break;
            }
        }

        chart.Regions = regions;
        chart.Points = points;
        return chart;
    }

    public MermaidBlock Block { get; }

    public QuadrantConfig Config { get; }

    /// <summary>What the x-axis's low end, on the left, and high end, on the right, say — null where not written.</summary>
    public QuadrantText? Left { get; private set; }
    public QuadrantText? Right { get; private set; }

    /// <summary>What the y-axis's low end, at the bottom, and high end, at the top, say.</summary>
    public QuadrantText? Bottom { get; private set; }
    public QuadrantText? Top { get; private set; }

    /// <summary>Each quadrant's caption — the first top right, then anticlockwise: top left, bottom left, bottom right.</summary>
    public IReadOnlyList<QuadrantText?> Regions { get; private set; } = [null, null, null, null];

    public IReadOnlyList<QuadrantPoint> Points { get; private set; } = [];

    /// <summary>Whether the x-axis's words go over the chart: as the front matter says, or else only where there are no points.</summary>
    public bool XAxisOnTop => Config.XAxisOnTop ?? Points.Count == 0;

    private static string? Word(ContentPart part) => part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text;

    private static string? Name(ContentPart line) => line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text;

    private static ContentPart? Properties(ContentPart line) => line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Properties);

    private static QuadrantText? Text(ContentPart line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == QuadrantKinds.Text && child.Role == role) is { } text && text.Words() is { } says
            ? new QuadrantText(line, says, text.Hole())
            : null;
}
