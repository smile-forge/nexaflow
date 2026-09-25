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
/// for each flow as thick as it is worth. The columns stand a set width apart, the fullest of them fills the height, the
/// nodes of each are ordered and then spread by the heights of what they are joined to, and the ribbons leave and arrive
/// stacked in the order of what they join.
///
/// <para>
/// <strong>Two layers.</strong> The ribbons are drawn under the bars, so a node is always visible where its flows meet it;
/// a ribbon stands for the row it was written on and a bar for the row that first named it, so pressing either means the
/// line it came from. What a node is called is the characters written, typed into where it is drawn.
/// </para>
/// </summary>
internal sealed class SankeyBuilder : MermaidBuilder
{
    /// <summary>How big the diagram is drawn before the front matter or the room says otherwise.</summary>
    private const double Wide = 720;
    private const double Tall = 440;

    /// <summary>The least the columns stand apart, however narrow the room — enough for the name of a node between two of them.</summary>
    private const double Apart = 110;

    /// <summary>How many times each column is set by its neighbours each way before the order down it is kept.</summary>
    private const int Passes = 4;

    /// <summary>How many times the nodes are drawn towards what they are joined to, and how much less each time than the last.</summary>
    private const int Relaxed = 6;
    private const double Easing = 0.8;

    private const double TextSize = 12;

    /// <summary>The clear air between a bar and what is written beside it.</summary>
    private const double Gap = 6;

    /// <summary>The least a ribbon is drawn at, so a flow worth almost nothing is still a flow to look at.</summary>
    private const double Thinnest = 1;

    /// <summary>How solid a ribbon is.</summary>
    private const double Wash = 0.5;

    internal SankeyBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    /// <summary>
    /// A node. Nothing declares one: it is a name the flows are written between, and it stands for the first flow that wrote
    /// it.
    /// </summary>
    /// <param name="said">The name as it was first written, which is what typing beside the node changes.</param>
    /// <param name="order">Where it comes among the nodes, which is the colour it takes and the order it is stacked in.</param>
    private sealed class Node(string name, ContentPart said, int order)
    {
        public string Name { get; } = name;

        public ContentPart Said { get; } = said;

        public int Order { get; } = order;

        /// <summary>What flows into it, and out of it, of the flows drawn.</summary>
        public double Into { get; set; }

        public double OutOf { get; set; }

        /// <summary>What it is worth: whatever flows into it, or out of it, whichever is the more.</summary>
        public double Worth => Math.Max(Into, OutOf);
    }

    /// <summary>One flow: the row it was written on, where it comes from and goes, and what it is worth — nought where no value is written or it is no number.</summary>
    private sealed record Flow(ContentPart Part, string From, string To, double Worth)
    {
        /// <summary>Whether it is worth drawing a ribbon for.</summary>
        public bool Drawn => Worth > 0 && From.Length > 0 && To.Length > 0;
    }

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var (nodes, flows) = Read();
        var drawn = nodes.Where(node => node.Worth > 0).ToList();

        // Nothing worth drawing is the source: what the reader wants back is their own rows.
        if (drawn.Count == 0) return AsWritten(build);

        var config = Configured(SankeyConfig.Default);
        var named = drawn.ToDictionary(node => node.Name, StringComparer.Ordinal);
        var columns = Columns(flows, drawn, config.Alignment);
        var said = drawn.ToDictionary(node => node.Name, node => Labelled(config, node), StringComparer.Ordinal);
        var tall = Math.Max(config.Height ?? Tall, 80);
        var (tops, heights, scale) = Stacked(flows, drawn, columns, tall, config);
        var (step, offset, wide, right) = Across(drawn, said, columns, config);

        var bars = drawn.ToDictionary(node => node.Name,
                                      node => new Rect(offset + (columns[node.Name] * step), tops[node.Name], config.NodeWidth, heights[node.Name]),
                                      StringComparer.Ordinal);

        var ribbons = Ribbons(flows, bars, columns, scale);

        build.Open(SankeyPiece.Flows, part: null, stops: Stops.None);
        foreach (var (flow, shape) in ribbons) Ribbon(build, config, named, flow, shape);
        build.Close();

        build.Open(SankeyPiece.Nodes, part: null, stops: Stops.None);
        foreach (var node in drawn) Bar(build, config, node, bars[node.Name], said[node.Name], right[node.Name]);
        build.Close();

        return new Size(wide, tall);
    }

    /// <summary>The nodes, in the order they are first written, and the flows, in the order they are written.</summary>
    private (List<Node> Nodes, List<Flow> Flows) Read()
    {
        var nodes = new List<Node>();
        var named = new Dictionary<string, Node>(StringComparer.Ordinal);
        var flows = new List<Flow>();

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            if (part.Kind != SankeyKinds.Flow) continue;

            var names = part.Children.Where(child => child.Kind == MermaidKinds.Name);
            var (source, target) = (names.ElementAtOrDefault(0).Words(), names.ElementAtOrDefault(1).Words());
            var flow = new Flow(part, Says(source), Says(target), part.Inner(MermaidKinds.Number).Number() ?? 0);

            flows.Add(flow);
            Name(flow.From, source);
            Name(flow.To, target);

            if (!flow.Drawn) continue;

            named[flow.From].OutOf += flow.Worth;
            named[flow.To].Into += flow.Worth;
        }

        return (nodes, flows);

        void Name(string name, ContentPart? said)
        {
            if (name.Length == 0 || said is null || named.ContainsKey(name)) return;

            named[name] = new Node(name, said, nodes.Count);
            nodes.Add(named[name]);
        }
    }

    /// <summary>
    /// What a name says: what is between its quotes where it has them, with a quote written twice standing for one — and
    /// without the space either side of it, as Mermaid reads a field. That is the very string written wherever there is
    /// nothing to take out of it.
    /// </summary>
    private static string Says(ContentPart? words) =>
        words is null ? string.Empty : words.Text.Replace(SankeyGrammar.Quoted, "\"", StringComparison.Ordinal).Trim();

    // ── Where everything sits ───────────────────────────────────────────────

    /// <summary>
    /// Which column each node is in: as far along as the longest run of flows reaching it, and then wherever the front
    /// matter's alignment asks for it — a node nothing leaves pushed to the far side where it says to justify, and a node
    /// nothing reaches pulled up against what it feeds where it says to centre.
    /// </summary>
    private static Dictionary<string, int> Columns(IReadOnlyList<Flow> written, IReadOnlyList<Node> drawn, SankeyAlignment alignment)
    {
        var at = drawn.ToDictionary(node => node.Name, _ => 0, StringComparer.Ordinal);
        var flows = written.Where(flow => flow.Drawn && at.ContainsKey(flow.From) && at.ContainsKey(flow.To)).ToList();

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

        switch (alignment)
        {
            case SankeyAlignment.Justify:
                foreach (var node in drawn.Where(node => node.OutOf <= 0)) at[node.Name] = last;
                break;

            case SankeyAlignment.Centre:
                foreach (var node in drawn.Where(node => node.Into <= 0))
                {
                    var reaches = flows.Where(flow => flow.From == node.Name).Select(flow => at[flow.To]).ToList();
                    if (reaches.Count > 0) at[node.Name] = Math.Max(0, reaches.Min() - 1);
                }

                break;

            case SankeyAlignment.Right:
                foreach (var node in drawn.Where(node => node.OutOf <= 0)) at[node.Name] = last;
                foreach (var flow in Enumerable.Range(0, drawn.Count).SelectMany(_ => flows).Where(flow => at[flow.From] >= at[flow.To]))
                    at[flow.From] = at[flow.To] - 1;

                break;
        }

        var least = at.Values.Min();
        return at.ToDictionary(node => node.Key, node => node.Value - least, StringComparer.Ordinal);
    }

    /// <summary>
    /// Where each node's bar stands down the page and how tall it is, and how much height a flow is drawn for what it is worth —
    /// the same for the ribbons as for the bars they meet.
    ///
    /// <para>
    /// Every column is drawn to the same scale, so the fullest of them fills the height, and each column starts stacked about the
    /// middle of it. The order down a column is worked out rather than written: a node goes down its column as far as what flows
    /// into it comes from, and then as far as what it flows on to goes — a few times over each way — so the ribbons run across
    /// rather than over one another, which is most of what makes a sankey diagram read.
    /// </para>
    /// </summary>
    private static (Dictionary<string, double> Tops, Dictionary<string, double> Heights, double Scale) Stacked(
        IReadOnlyList<Flow> written, IReadOnlyList<Node> drawn, IReadOnlyDictionary<string, int> columns, double tall, SankeyConfig config)
    {
        var last = columns.Values.Max();
        var stacks = Enumerable.Range(0, last + 1)
            .Select(column => drawn.Where(node => columns[node.Name] == column).OrderBy(node => node.Order).ToList())
            .ToList();

        var scale = stacks
            .Where(stack => stack.Sum(node => node.Worth) > 0)
            .Select(stack => (tall - (config.NodePadding * Math.Max(0, stack.Count - 1))) / stack.Sum(node => node.Worth))
            .DefaultIfEmpty(1)
            .Min();

        var heights = drawn.ToDictionary(node => node.Name, node => Math.Max(Thinnest, node.Worth * scale), StringComparer.Ordinal);
        var tops = new Dictionary<string, double>(StringComparer.Ordinal);
        var flows = written.Where(flow => flow.Drawn && heights.ContainsKey(flow.From) && heights.ContainsKey(flow.To)).ToList();

        foreach (var stack in stacks) Settle(stack);

        for (var pass = 0; pass < Passes; pass++)
        {
            // Onwards, each column set by where what flows into it comes from; then back, each set by where what it flows to goes.
            for (var column = 1; column <= last; column++) Sort(stacks[column], flow => flow.To, flow => flow.From);
            for (var column = last - 1; column >= 0; column--) Sort(stacks[column], flow => flow.From, flow => flow.To);
        }

        // Then the nodes of a column with room to spare are spread down it rather than packed about its middle: each drawn towards
        // the height of what it is joined to, a little less each time round, and the column set clear again after every move. A
        // ribbon between two nodes at much the same height runs level, and ribbons that run level lie beside one another rather
        // than across — which is d3-sankey's relaxation, the layout Mermaid draws with.
        for (var (pass, pull) = (0, 1.0); pass < Relaxed; pass++, pull *= Easing)
        {
            for (var column = 1; column <= last; column++) Relax(stacks[column], flow => flow.To, flow => flow.From, pull);
            for (var column = last - 1; column >= 0; column--) Relax(stacks[column], flow => flow.From, flow => flow.To, pull);
        }

        return (tops, heights, scale);

        double Middle(string name) => tops[name] + (heights[name] / 2);

        // The middle of what a node is joined to, each by what it is worth — or its own, joined to nothing that way.
        double Wanted(Node node, Func<Flow, string> own, Func<Flow, string> other)
        {
            var joined = flows.Where(flow => own(flow) == node.Name && tops.ContainsKey(other(flow))).ToList();
            var worth = joined.Sum(flow => flow.Worth);

            return worth > 0 ? joined.Sum(flow => Middle(other(flow)) * flow.Worth) / worth : Middle(node.Name);
        }

        void Relax(List<Node> stack, Func<Flow, string> own, Func<Flow, string> other, double pull)
        {
            foreach (var node in stack) tops[node.Name] += (Wanted(node, own, other) - Middle(node.Name)) * pull;

            var ordered = stack.OrderBy(node => tops[node.Name]).ToList();
            stack.Clear();
            stack.AddRange(ordered);
            Clear(stack);
        }

        // Down the column, each node clear of the one above it; then, where that ran off the foot, back up from the foot.
        void Clear(List<Node> stack)
        {
            var below = 0.0;
            foreach (var node in stack)
            {
                tops[node.Name] = Math.Max(tops[node.Name], below);
                below = tops[node.Name] + heights[node.Name] + config.NodePadding;
            }

            var above = tall;
            for (var at = stack.Count - 1; at >= 0; at--)
            {
                var node = stack[at];
                tops[node.Name] = Math.Max(0, Math.Min(tops[node.Name], above - heights[node.Name]));
                above = tops[node.Name] - config.NodePadding;
            }
        }

        void Settle(List<Node> stack)
        {
            var taken = stack.Sum(node => heights[node.Name]) + (config.NodePadding * Math.Max(0, stack.Count - 1));
            var down = Math.Max(0, (tall - taken) / 2);

            foreach (var node in stack)
            {
                tops[node.Name] = down;
                down += heights[node.Name] + config.NodePadding;
            }
        }

        void Sort(List<Node> stack, Func<Flow, string> own, Func<Flow, string> other)
        {
            // The middle of what it is joined to, each by what it is worth; a node joined to nothing that way keeps where it is.
            var wanted = stack.ToDictionary(node => node.Name, node => Wanted(node, own, other), StringComparer.Ordinal);

            var ordered = stack.OrderBy(node => wanted[node.Name]).ThenBy(node => node.Order).ToList();
            stack.Clear();
            stack.AddRange(ordered);
            Settle(stack);
        }
    }

    /// <summary>
    /// How far apart the columns stand, where the first of them starts, how wide the whole diagram comes to, and which side of
    /// its bar each node's name goes.
    ///
    /// <para>
    /// The columns stand a width of their own apart — the width the diagram is drawn at shared among them, and never less than
    /// room for what is written between two of them — rather than being squeezed into whatever is left once the names at either
    /// edge are set, which is what crowds the middle of a diagram with many columns into a heap. A name goes to the right of its
    /// bar, into the gap before the next column, and the last column's to the left of theirs: every name then has a gap to
    /// itself, where names set either side of the middle would meet in the one gap between two middle columns.
    /// </para>
    /// </summary>
    private (double Step, double Offset, double Wide, Dictionary<string, bool> Right) Across(
        IReadOnlyList<Node> drawn, IReadOnlyDictionary<string, IReadOnlyList<DiagramWords>> said,
        IReadOnlyDictionary<string, int> columns, SankeyConfig config)
    {
        var last = columns.Values.Max();
        var step = last == 0 ? 0 : Math.Max(Apart, (config.Width ?? Wide) / (last + 1));
        var right = drawn.ToDictionary(node => node.Name, node => last == 0 || columns[node.Name] < last, StringComparer.Ordinal);

        // Past the diagram's own width only where a name is wider than the gap it goes in, or a lone column's names.
        var (least, most) = (0.0, (last * step) + config.NodeWidth);
        foreach (var node in drawn)
        {
            var words = DiagramWords.Taken(said[node.Name]).Width + Gap;
            var x = columns[node.Name] * step;

            if (right[node.Name]) most = Math.Max(most, x + config.NodeWidth + words);
            else least = Math.Min(least, x - words);
        }

        return (step, -least, most - least, right);
    }

    /// <summary>
    /// The ribbon each flow is drawn as: as thick as it is worth, leaving its source and arriving at its target stacked in the
    /// order of where each goes to or comes from — the flows off one bar in the order of the bars they reach, so they fan out
    /// rather than cross on their way.
    /// </summary>
    private static List<(Flow Flow, Geometry Shape)> Ribbons(IReadOnlyList<Flow> written, IReadOnlyDictionary<string, Rect> bars,
                                                       IReadOnlyDictionary<string, int> columns, double scale)
    {
        var drawn = written.Where(flow => flow.Drawn && bars.ContainsKey(flow.From) && bars.ContainsKey(flow.To)).ToList();

        double Middle(string name) => bars[name].Y + (bars[name].Height / 2);

        var starts = new Dictionary<Flow, double>();
        var stops = new Dictionary<Flow, double>();

        foreach (var leaving in drawn.GroupBy(flow => flow.From))
        {
            var down = bars[leaving.Key].Y;
            foreach (var flow in leaving.OrderBy(flow => Middle(flow.To)))
            {
                starts[flow] = down;
                down += Math.Max(Thinnest, flow.Worth * scale);
            }
        }

        foreach (var arriving in drawn.GroupBy(flow => flow.To))
        {
            var down = bars[arriving.Key].Y;
            foreach (var flow in arriving.OrderBy(flow => Middle(flow.From)))
            {
                stops[flow] = down;
                down += Math.Max(Thinnest, flow.Worth * scale);
            }
        }

        var ribbons = new List<(Flow, Geometry)>();

        foreach (var flow in drawn)
        {
            var (from, to) = (bars[flow.From], bars[flow.To]);
            var thick = Math.Max(Thinnest, flow.Worth * scale);

            // A flow that runs back the way it came leaves the right of its source all the same, so it is drawn going round.
            var forward = columns[flow.To] >= columns[flow.From];
            ribbons.Add((flow, Band(forward ? from.Right : from.Left, starts[flow], forward ? to.Left : to.Right, stops[flow], thick)));
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

    private void Ribbon(LayoutBuilder build, SankeyConfig config, IReadOnlyDictionary<string, Node> named, Flow flow, Geometry shape)
    {
        build.Open(SankeyPiece.Flow, flow.Part, stops: Stops.None);
        build.Draw(new GeometryMark(shape, Coloured(config, named, flow, shape.Bounds), null, 0));
        build.Occupies(shape);
        build.Close();
    }

    /// <summary>A node: its bar, and what it is called beside it — to the <paramref name="right"/> of it, or to the left.</summary>
    private void Bar(LayoutBuilder build, SankeyConfig config, Node node, Rect bar, IReadOnlyList<DiagramWords> said, bool right)
    {
        var last = !right;
        var taken = DiagramWords.Taken(said);
        var room = new Rect(last ? bar.Left - Gap - taken.Width : bar.Right + Gap,
                            bar.Y + ((bar.Height - taken.Height) / 2), taken.Width, taken.Height);

        build.Open(SankeyPiece.Node, node.Said, stops: Stops.None);
        build.Draw(new GeometryMark(Outline(bar), Fill(config, node), null, 0));
        build.Occupies(Outline(bar));

        if (config.Labels == SankeyLabels.Outlined)
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
    private IReadOnlyList<DiagramWords> Labelled(SankeyConfig config, Node node)
    {
        var ink = Palette.Text;

        // A name written with a quote in it says one quote where it was written twice, so those characters are not the
        // characters drawn and the name is shown rather than typed into.
        var name = node.Said.Text.Contains(SankeyGrammar.Quoted, StringComparison.Ordinal)
            ? Worked(node.Name, node.Said, TextSize, ink)
            : Written(node.Said, hole: null, TextSize, ink);

        if (!config.ShowValues) return [name];

        var worth = config.Prefix + Said(node.Worth) + config.Suffix;
        return [name, Worked(worth, node.Said, TextSize - 1, Palette.TextMuted)];
    }

    private static string Said(double worth) =>
        worth.ToString(Math.Abs(worth - Math.Round(worth)) < 0.0005 ? "0" : "0.###", CultureInfo.CurrentCulture);

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>What a node is drawn in: the colour the front matter writes for it, or its own from the series.</summary>
    private Brush Fill(SankeyConfig config, Node node) =>
        Ink.Written(config.NodeColours.GetValueOrDefault(node.Name)) ?? Ink.Series(node.Order);

    /// <summary>
    /// What a ribbon is drawn in: the colour of the node it leaves, the one it reaches, one written for all of them, or —
    /// as Mermaid draws one by default — from the first to the second along its length. Always half-strength against the bars
    /// it runs between, which are drawn in their colours whole: the bars read as the light ends of the flows, where a flow
    /// starts, passes through and ends, and the ribbons as the darker body of it.
    /// </summary>
    private Brush Coloured(SankeyConfig config, IReadOnlyDictionary<string, Node> named, Flow flow, Rect bounds)
    {
        var from = named.TryGetValue(flow.From, out var source) ? Fill(config, source) : Palette.TextMuted;
        var to = named.TryGetValue(flow.To, out var target) ? Fill(config, target) : Palette.TextMuted;

        switch (config.LinkColour)
        {
            case SankeyLinkColour.Source: return DiagramInk.Faded(from, Wash);
            case SankeyLinkColour.Target: return DiagramInk.Faded(to, Wash);
            case SankeyLinkColour.Written: return DiagramInk.Faded(Ink.Written(config.LinkWritten) ?? Palette.TextMuted, Wash);
        }

        if (bounds.Width <= 0 || from is not SolidColorBrush start || to is not SolidColorBrush stop) return DiagramInk.Faded(from, Wash);

        // The fade is the brush's own, not its stops': a colour taken off a faded brush is the colour whole, and a gradient of
        // those is as bright as the bars at either end of it.
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops = { new GradientStop(start.Color, 0), new GradientStop(stop.Color, 1) },
            Opacity = Wash,
        };

        gradient.Freeze();
        return gradient;
    }
}
