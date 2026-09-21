using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sankey;

/// <summary>The pieces a sankey diagram's layout is made of — its layers, and what is in them.</summary>
public static class SankeyPiece
{
    /// <summary>The ribbons, which are the flows themselves.</summary>
    public const string Flows = "Flows";

    /// <summary>One ribbon. It stands for the row it was written on, so pressing it means that flow.</summary>
    public const string Flow = "Flow";

    /// <summary>The bars, one for each node the flows are written between.</summary>
    public const string Nodes = "Nodes";

    /// <inheritdoc cref="Nodes"/>
    public const string Node = "Node";

    /// <summary>What a node is called, and what it is worth, beside its bar.</summary>
    public const string Label = "Label";
}

/// <summary>
/// Draws a <c>sankey-beta</c> block: a bar for each node, in a column as far along as the flows into it reach, and a ribbon
/// for each flow as thick as it is worth. The columns are as far apart as the room allows, the tallest of them fills the
/// height, and the ribbons leave and arrive stacked in the order they are written.
///
/// <para>
/// <strong>Two layers.</strong> The ribbons are drawn under the bars, so a node is always visible where its flows meet it;
/// a ribbon stands for the row it was written on and a bar for the row that first named it, so pressing either means the
/// line it came from. What a node is called is the characters written, typed into where it is drawn.
/// </para>
/// </summary>
internal sealed class SankeyBuilder : MermaidBuilder<SankeyChart>
{
    /// <summary>How big the diagram is drawn before the front matter or the room says otherwise.</summary>
    private const double Wide = 640;
    private const double Tall = 380;

    private const double TextSize = 12;

    /// <summary>The clear air between a bar and what is written beside it.</summary>
    private const double Gap = 6;

    /// <summary>The least a ribbon is drawn at, so a flow worth almost nothing is still a flow to look at.</summary>
    private const double Thinnest = 1;

    /// <summary>How solid a ribbon is.</summary>
    private const double Wash = 0.45;

    internal SankeyBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override SankeyChart Of(MermaidBlock block) => SankeyChart.Of(block);

    protected override Size Draw(SankeyChart chart, LayoutBuilder build)
    {
        var drawn = chart.Nodes.Where(node => chart.Worth(node) > 0).ToList();

        // Nothing worth drawing is the source: what the reader wants back is their own rows.
        if (drawn.Count == 0) return AsWritten(build);

        var config = chart.Config;
        var columns = Columns(chart, drawn);
        var said = drawn.ToDictionary(node => node.Name, node => Labelled(chart, node), StringComparer.Ordinal);

        // The widest label on each side says how much room the bars have between them.
        var last = columns.Values.Max();
        var left = Widest(drawn, said, columns, column => column == 0);
        var right = Widest(drawn, said, columns, column => column == last);

        var wide = Math.Max(config.Width ?? Wide, (last + 1) * 80);
        var tall = Math.Max(config.Height ?? Tall, 80);
        var across = new Rect(left + Gap, 0, Math.Max(40, wide - left - right - (Gap * 2)), tall);

        var (bars, scale) = Bars(chart, drawn, columns, across, config);
        var ribbons = Ribbons(chart, bars, columns, scale);

        build.Open(SankeyPiece.Flows, part: null, stops: Stops.None);
        foreach (var (flow, shape) in ribbons) Ribbon(build, chart, flow, shape);
        build.Close();

        build.Open(SankeyPiece.Nodes, part: null, stops: Stops.None);
        foreach (var node in drawn) Bar(build, chart, node, bars[node.Name], said[node.Name], columns[node.Name] == last);
        build.Close();

        return new Size(wide, tall);
    }

    // ── Where everything sits ───────────────────────────────────────────────

    /// <summary>
    /// Which column each node is in: as far along as the longest run of flows reaching it, and then wherever the front
    /// matter's alignment asks for it — a node nothing leaves pushed to the far side where it says to justify, and a node
    /// nothing reaches pulled up against what it feeds where it says to centre.
    /// </summary>
    private static Dictionary<string, int> Columns(SankeyChart chart, IReadOnlyList<SankeyNode> drawn)
    {
        var at = drawn.ToDictionary(node => node.Name, _ => 0, StringComparer.Ordinal);
        var flows = chart.Flows.Where(flow => flow.Drawn && at.ContainsKey(flow.From) && at.ContainsKey(flow.To)).ToList();

        // Settled by going round until nothing moves, which a run of flows round in a circle cannot make go on forever.
        for (var round = 0; round < drawn.Count; round++)
        {
            var moved = false;

            foreach (var flow in flows.Where(flow => at[flow.To] <= at[flow.From]))
            {
                at[flow.To] = at[flow.From] + 1;
                moved = true;
            }

            if (!moved) break;
        }

        var last = at.Values.Max();

        switch (chart.Config.Alignment)
        {
            case SankeyAlignment.Justify:
                foreach (var node in drawn.Where(node => chart.OutOf(node) <= 0)) at[node.Name] = last;
                break;

            case SankeyAlignment.Centre:
                foreach (var node in drawn.Where(node => chart.Into(node) <= 0))
                {
                    var reaches = flows.Where(flow => flow.From == node.Name).Select(flow => at[flow.To]).ToList();
                    if (reaches.Count > 0) at[node.Name] = Math.Max(0, reaches.Min() - 1);
                }

                break;

            case SankeyAlignment.Right:
                foreach (var node in drawn.Where(node => chart.OutOf(node) <= 0)) at[node.Name] = last;
                foreach (var flow in Enumerable.Range(0, drawn.Count).SelectMany(_ => flows).Where(flow => at[flow.From] >= at[flow.To]))
                    at[flow.From] = at[flow.To] - 1;

                break;
        }

        var least = at.Values.Min();
        return at.ToDictionary(node => node.Key, node => node.Value - least, StringComparer.Ordinal);
    }

    /// <summary>
    /// The bar each node is drawn as — its column across, and its share of the height down — and how much height a flow is
    /// drawn for what it is worth, which is the same for the ribbons as for the bars they meet.
    /// </summary>
    private static (Dictionary<string, Rect> Bars, double Scale) Bars(SankeyChart chart, IReadOnlyList<SankeyNode> drawn,
                                                                      IReadOnlyDictionary<string, int> columns, Rect across, SankeyConfig config)
    {
        var last = columns.Values.Max();
        var step = last == 0 ? 0 : (across.Width - config.NodeWidth) / last;

        // Every column is drawn to the same scale, so the tallest of them is what fills the height.
        var totals = Enumerable.Range(0, last + 1)
            .Select(column => drawn.Where(node => columns[node.Name] == column).Sum(chart.Worth))
            .ToList();

        var counts = Enumerable.Range(0, last + 1).Select(column => drawn.Count(node => columns[node.Name] == column)).ToList();
        var scale = Enumerable.Range(0, last + 1)
            .Where(column => totals[column] > 0)
            .Select(column => (across.Height - (config.NodePadding * Math.Max(0, counts[column] - 1))) / totals[column])
            .DefaultIfEmpty(1)
            .Min();

        var bars = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var down = new double[last + 1];

        foreach (var node in drawn.OrderBy(node => columns[node.Name]).ThenBy(node => node.Order))
        {
            var column = columns[node.Name];
            var height = Math.Max(Thinnest, chart.Worth(node) * scale);

            bars[node.Name] = new Rect(across.X + (column * step), across.Y + down[column], config.NodeWidth, height);
            down[column] += height + config.NodePadding;
        }

        return (bars, scale);
    }

    /// <summary>
    /// The ribbon each flow is drawn as: as thick as it is worth, leaving its source and arriving at its target stacked in
    /// the order the flows are written.
    /// </summary>
    private static List<(SankeyFlow Flow, Geometry Shape)> Ribbons(SankeyChart chart, IReadOnlyDictionary<string, Rect> bars,
                                                                   IReadOnlyDictionary<string, int> columns, double scale)
    {
        var drawn = chart.Flows.Where(flow => flow.Drawn && bars.ContainsKey(flow.From) && bars.ContainsKey(flow.To)).ToList();

        var leaving = new Dictionary<string, double>(StringComparer.Ordinal);
        var arriving = new Dictionary<string, double>(StringComparer.Ordinal);
        var ribbons = new List<(SankeyFlow, Geometry)>();

        foreach (var flow in drawn)
        {
            var (from, to) = (bars[flow.From], bars[flow.To]);
            var thick = Math.Max(Thinnest, flow.Worth * scale);

            var start = from.Y + leaving.GetValueOrDefault(flow.From);
            var stop = to.Y + arriving.GetValueOrDefault(flow.To);

            leaving[flow.From] = leaving.GetValueOrDefault(flow.From) + thick;
            arriving[flow.To] = arriving.GetValueOrDefault(flow.To) + thick;

            // A flow that runs back the way it came leaves the right of its source all the same, so it is drawn going round.
            var forward = columns[flow.To] >= columns[flow.From];
            ribbons.Add((flow, Band(forward ? from.Right : from.Left, start, forward ? to.Left : to.Right, stop, thick)));
        }

        return ribbons;
    }

    /// <summary>A ribbon from one bar to another: bowed out of each end, and as thick all the way as it is at them.</summary>
    private static Geometry Band(double x0, double y0, double x1, double y1, double thick)
    {
        var bend = (x1 - x0) / 2;
        var shape = new StreamGeometry();

        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(x0, y0), isFilled: true, isClosed: true);
            pen.BezierTo(new Point(x0 + bend, y0), new Point(x1 - bend, y1), new Point(x1, y1), isStroked: false, isSmoothJoin: false);
            pen.LineTo(new Point(x1, y1 + thick), isStroked: false, isSmoothJoin: false);
            pen.BezierTo(new Point(x1 - bend, y1 + thick), new Point(x0 + bend, y0 + thick), new Point(x0, y0 + thick), isStroked: false, isSmoothJoin: false);
        }

        shape.Freeze();
        return shape;
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    private void Ribbon(LayoutBuilder build, SankeyChart chart, SankeyFlow flow, Geometry shape)
    {
        build.Open(SankeyPiece.Flow, flow.Part, stops: Stops.None);
        build.Draw(new GeometryMark(shape, Coloured(chart, flow, shape.Bounds), null, 0));
        build.Occupies(shape);
        build.Close();
    }

    /// <summary>A node: its bar, and what it is called beside it — on the far side of it from the flows it meets.</summary>
    private void Bar(LayoutBuilder build, SankeyChart chart, SankeyNode node, Rect bar, IReadOnlyList<DiagramWords> said, bool last)
    {
        var taken = DiagramWords.Taken(said);
        var room = new Rect(last ? bar.Left - Gap - taken.Width : bar.Right + Gap,
                            bar.Y + ((bar.Height - taken.Height) / 2), taken.Width, taken.Height);

        build.Open(SankeyPiece.Node, node.Said, stops: Stops.None);
        build.Draw(new GeometryMark(Outline(bar), Fill(chart, node), null, 0));
        build.Occupies(Outline(bar));

        if (chart.Config.Labels == SankeyLabels.Outlined)
            build.Draw(new GeometryMark(Outline(Rect.Inflate(room, 3, 1)), Palette.CodeBg, Palette.CodeBorder, 1));

        foreach (var (words, at, kind) in DiagramWords.Placed(said, room, SankeyPiece.Label, last ? TextAlignment.Right : TextAlignment.Left))
            words.Set(build, at, kind);

        build.Close();
    }

    private static Geometry Outline(Rect bounds)
    {
        var shape = new RectangleGeometry(bounds);
        shape.Freeze();
        return shape;
    }

    // ── What is written beside a node ───────────────────────────────────────

    /// <summary>What a node says: its name as it was written, and what it is worth where the front matter shows values.</summary>
    private IReadOnlyList<DiagramWords> Labelled(SankeyChart chart, SankeyNode node)
    {
        var ink = Palette.Text;

        // A name written with a quote in it says one quote where it was written twice, so those characters are not the
        // characters drawn and the name is shown rather than typed into.
        var name = node.Said.Text.Contains(SankeyGrammar.Quoted, StringComparison.Ordinal)
            ? Worked(node.Name, node.Said, TextSize, ink)
            : Written(node.Said, hole: null, TextSize, ink);

        if (!chart.Config.ShowValues) return [name];

        var worth = chart.Config.Prefix + Said(chart.Worth(node)) + chart.Config.Suffix;
        return [name, Worked(worth, node.Said, TextSize - 1, Palette.TextMuted)];
    }

    private static string Said(double worth) =>
        worth.ToString(Math.Abs(worth - Math.Round(worth)) < 0.0005 ? "0" : "0.###", CultureInfo.CurrentCulture);

    /// <summary>How wide what is written beside a node runs, for the nodes on one side of the diagram.</summary>
    private static double Widest(IReadOnlyList<SankeyNode> drawn, IReadOnlyDictionary<string, IReadOnlyList<DiagramWords>> said,
                                 IReadOnlyDictionary<string, int> columns, Func<int, bool> side) =>
        drawn.Where(node => side(columns[node.Name]))
            .Select(node => DiagramWords.Taken(said[node.Name]).Width)
            .DefaultIfEmpty(0)
            .Max();

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>What a node is drawn in: the colour the front matter writes for it, or its own from the series.</summary>
    private Brush Fill(SankeyChart chart, SankeyNode node) =>
        Ink.Written(chart.Config.NodeColours.GetValueOrDefault(node.Name)) ?? Ink.Series(node.Order);

    /// <summary>
    /// What a ribbon is drawn in: the colour of the node it leaves, the one it reaches, one written for all of them, or —
    /// as Mermaid draws one by default — from the first to the second along its length.
    /// </summary>
    private Brush Coloured(SankeyChart chart, SankeyFlow flow, Rect bounds)
    {
        var from = chart.Node(flow.From) is { } source ? Fill(chart, source) : Palette.TextMuted;
        var to = chart.Node(flow.To) is { } target ? Fill(chart, target) : Palette.TextMuted;

        switch (chart.Config.LinkColour)
        {
            case SankeyLinkColour.Source: return DiagramInk.Faded(from, Wash);
            case SankeyLinkColour.Target: return DiagramInk.Faded(to, Wash);
            case SankeyLinkColour.Written: return DiagramInk.Faded(Ink.Written(chart.Config.LinkWritten) ?? Palette.TextMuted, Wash);
        }

        if (bounds.Width <= 0 || Shade(from) is not { } start || Shade(to) is not { } stop) return DiagramInk.Faded(from, Wash);

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops = { new GradientStop(start, 0), new GradientStop(stop, 1) },
        };

        gradient.Freeze();
        return gradient;
    }

    /// <summary>The colour a node's own is faded to where a ribbon is drawn in it, or null for one that is no flat colour.</summary>
    private static Color? Shade(Brush brush) => DiagramInk.Faded(brush, Wash) is SolidColorBrush solid ? solid.Color : null;
}
