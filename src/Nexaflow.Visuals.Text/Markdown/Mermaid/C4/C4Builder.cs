using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

/// <summary>
/// Draws a <c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>, <c>C4Dynamic</c> or <c>C4Deployment</c>: the cards, the
/// boundaries holding them, and the relationships between them.
///
/// <para>
/// A C4 diagram <em>is</em> a graph with richer boxes, so it needs no layout of its own: the cards are cells,
/// the boundaries are cells holding cells, the relationships are what joins them, and <see cref="DiagramLayers"/> lays the
/// lot out — the same layered layout a class diagram and a flowchart are laid out by.
/// </para>
///
/// <para>
/// The card itself is <see cref="DiagramCard"/>'s and its colour <see cref="DiagramTone"/>'s, both shared with the C4 sequence, so
/// a person looks the same and grades the same whichever of the two a reader wrote.
/// </para>
/// </summary>
internal sealed class C4Builder : MermaidBuilder<C4Structure>
{
    /// <summary>How big a card's name, the line saying what it is, and the sentence under that are set.</summary>
    private const double TextSize = 12;
    private const double MetaSize = 10.5;
    private const double SaidSize = 11;

    /// <summary>And what is written on a relationship.</summary>
    private const double LabelSize = 11;

    /// <summary>The narrowest a card is drawn, so a one-word element still reads as a card.</summary>
    private const double Least = 150;

    /// <summary>Air inside a boundary, between its edge and its name.</summary>
    private const double Boxed = 14;

    /// <summary>The least air a boundary keeps between its edge and what it holds, so it reads as closed round them.</summary>
    private const double Closing = 16;

    /// <summary>How many goes a diagram gets at coming down to the room it was given.</summary>
    private const int Fittings = 4;

    /// <summary>
    /// Clear air either side of what is written between two ranks, so the line it is written on still shows, and enough of
    /// it runs straight for a bend in it to be a curve rather than a corner.
    /// </summary>
    private const double Clear = 40;

    /// <summary>And the air kept between two things written in the same gap, so neither reads as running into the other.</summary>
    private const double Apart = 10;



    private const double Thick = 1.5;

    /// <summary>How much of its colour a boundary's wash keeps, and the band across the top of it carrying its name.</summary>
    private const double Wash = 0.08;
    private const double Banded = 0.22;

    /// <summary>How wide what is written on a relationship runs before it wraps.</summary>
    private const double Widest = 160;

    private readonly DiagramTone ink;

    private C4Builder(ContentReading reading, DiagramLaying laying) : base(reading, laying) => this.ink = C4Grading.Of(laying.Palette);

    /// <summary>Lays a structural C4 diagram's source out. Never null, and never throws.</summary>
    public static Laid Build(ContentReading reading, DiagramLaying laying) => new C4Builder(reading, laying).Lay();

    /// <inheritdoc/>
    protected override C4Structure Of(MermaidBlock block) => C4Structure.Of(block);

    /// <inheritdoc/>
    protected override DiagramChart? Chart(C4Structure diagram) =>
        new([.. diagram.Nodes.Select(node => node.Id)], [.. diagram.Links.Select(link => (link.From, link.To))]);

    /// <inheritdoc/>
    protected override Size Draw(C4Structure diagram, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Empty) return AsWritten(build);

        diagram = Fitted(diagram);

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The relationships are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var over = DiagramConnector.Covered(routes.Select(route => (route.Along, route.Room)), Thick);

        // Everything is drawn where it is drawn: a boundary holds the cards inside it and the relationships that only exist
        // inside it, and one that crosses out of a boundary belongs to whatever holds both of its ends.
        build.Open(C4Piece.Elements, part: null, stops: Stops.None);
        foreach (var box in diagram.Within(null)) Held(build, diagram, plan, room, box, over, routes);
        foreach (var node in diagram.Inside(null)) Drawn(build, plan, room, node, over);
        Relations(build, routes, group: null);
        plan.Spill.Draw(build, room, Ink.Surface, new DiagramStroke(Palette.CodeBorder, 1, DiagramStroke.Dotted));
        build.Close();

        Keyed(diagram);

        return room.Size;
    }

    /// <summary>
    /// The diagram to measures that fit the room the block was given.
    ///
    /// <para>
    /// A drawing wider than the room is a drawing with its right-hand side cut off, so the cards wrap their words narrower
    /// and the gaps close up until it fits. It takes a few goes because what a card comes to is not a straight multiple of
    /// what it was allowed — words wrap where words wrap — and it stops short of measures too small to read, since a
    /// diagram that cannot be drawn small enough is better drawn too wide than illegibly.
    /// </para>
    /// </summary>
    private C4Structure Fitted(C4Structure diagram)
    {
        if (double.IsInfinity(Space)) return diagram;

        for (var attempt = 0; attempt < Fittings; attempt++)
        {
            var room = Reached(diagram, Laid(diagram));
            if (room.Size.Width <= Space) break;

            var config = diagram.Config;
            var by = Space / room.Size.Width;

            var narrowed = config with
            {
                Widest = Math.Max(Least, config.Widest * by),
                Apart = Math.Max(Apart, config.Apart * by),
            };

            if (narrowed.Widest >= config.Widest && narrowed.Apart >= config.Apart) break;

            diagram = diagram.Sized(narrowed);
        }

        return diagram;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    /// <summary>
    /// Everything measured and placed: a cell for each card and each boundary, a join for each relationship, and the layered
    /// layout run over the lot of them.
    /// </summary>
    private Plan Laid(C4Structure diagram)
    {
        var plan = new Plan();
        var cells = new List<DiagramCell>();

        foreach (var box in Nested(diagram, null))
        {
            var words = Naming(box, diagram.Config);
            var said = DiagramWords.Taken(words);

            var held = new Bound(box, words)
            {
                Cell = new DiagramCell(new Size(said.Width + (Boxed * 2), 0))
                {
                    Inside = box.Parent is { } parent && plan.Boxes.TryGetValue(parent, out var outer) ? outer.Cell : null,

                    // Never tighter than Closing, whatever boxMargin says: a boundary drawn close to the cards inside it
                    // reads as a line behind them rather than as something closed round them.
                    Pad = Math.Max(diagram.Config.Framed, Closing),
                    Heading = said.Height > 0 ? said.Height + Boxed : 0,
                    Way = null,
                },
            };

            plan.Boxes[box.Key] = held;
            cells.Add(held.Cell);
        }

        foreach (var node in diagram.Nodes)
        {
            // An element folded away is never given a cell, so the relationships to it have no end to meet and a
            // boundary holding nothing else closes up rather than standing empty.
            if (!Draws(node.Id)) continue;

            var sized = Measure(node, diagram.Config);
            sized.Cell = new DiagramCell(sized.Size)
            {
                Inside = node.Box is { } key && plan.Boxes.TryGetValue(key, out var box) ? box.Cell : null,
            };

            plan.Nodes.Add(sized);
            plan.Named.TryAdd(node.Id, sized);
            cells.Add(sized.Cell);
        }

        foreach (var link in diagram.Links)
        {
            plan.Said[link] = Says(link);

            if (!plan.Named.TryGetValue(link.From, out var from) || !plan.Named.TryGetValue(link.To, out var to)) continue;
            if (ReferenceEquals(from, to)) continue;

            // What is written on the line goes with it, so the layout holds the ranks it runs between far enough apart.
            plan.Joins[link] = new DiagramJoin(from.Cell, to.Cell)
            {
                Said = plan.Said[link].Count > 0 ? DiagramWords.Taken(plan.Said[link]) : default,
            };
        }

        var way = diagram.Way == C4Way.Right ? DiagramWay.Right : DiagramWay.Down;

        plan.Spill = Spilled(id => plan.Named.TryGetValue(id, out var sized) ? sized.Cell : null);
        cells.AddRange(plan.Spill.Cells);

        plan.Size = DiagramLayers.Lay(cells, [.. plan.Joins.Values, .. plan.Spill.Joins], way, diagram.Config.Apart + Apart,
                                      diagram.Config.Apart, ports: true);

        return plan;
    }

    /// <summary>The boundaries, each before the ones nested in it, so a nested one is measured after the box it sits in.</summary>
    private static IEnumerable<C4Bound> Nested(C4Structure diagram, string? inside)
    {
        foreach (var box in diagram.Within(inside))
        {
            yield return box;
            foreach (var held in Nested(diagram, box.Key)) yield return held;
        }
    }

    /// <summary>Everything the diagram means to draw, gathered so the whole of it is brought inside the box the block takes.</summary>
    private DiagramRoom Reached(C4Structure diagram, Plan plan)
    {
        var room = DiagramRoom.Round(diagram.Config.Padding, plan.Size,
                                     [.. plan.Nodes.Select(node => node.Cell), .. plan.Boxes.Values.Select(box => box.Cell)],
                                     plan.Joins.Select(join => (join.Value, plan.Said[join.Key])));

        // diagramMarginX asks for more air across the drawing than down it, and the room keeps one number for both — so the
        // extra is reached as a wider box round everything, which moves the drawing over as well as making room for it.
        var extra = diagram.Config.Across - diagram.Config.Padding;
        if (extra > 0) room.Reach(Rect.Inflate(room.Reached, extra, 0));

        return room;
    }

    // ── What a card comes to ────────────────────────────────────────────────

    /// <summary>
    /// A card measured: its name, the line saying what it is, the sentence under that, and how much room the whole of it
    /// takes once its outline has had what it needs.
    /// </summary>
    private Sized Measure(C4Node node, C4Metrics config)
    {
        var (fill, stroke, ink) = this.ink.Card(node.Tone, node.Fill, node.Border, node.Ink);
        var shape = Carded(node.Shape);
        var room = Math.Max(20, config.Wrapping - DiagramCard.Wider(shape));

        var rows = new List<(DiagramWords Words, string Kind)>();

        if (node.Said is { } said)
            rows.AddRange(Wrapped(said, null, TextSize, ink, room, FontWeights.SemiBold).Select(line => (line, MermaidPiece.Words)));
        else
            rows.Add((Worked(node.Id, node.Part, TextSize, ink, weight: FontWeights.SemiBold), MermaidPiece.Words));

        // The stereotype is worked out from several things written, so it is pressed rather than typed into.
        if (node.Stereotype is { Length: > 0 } says)
            rows.Add((Worked(says, node.Technology ?? node.Part, MetaSize, DiagramTone.Muted(ink)), C4Piece.Stereotype));

        if (node.Describes is { Length: > 0 } describes)
            rows.AddRange(Wrapped(describes, null, SaidSize, ink, room).Select(line => (line, C4Piece.Describes)));

        var taken = DiagramWords.Taken([.. rows.Select(row => row.Words)]);
        var wide = Math.Clamp(taken.Width + (config.Padded * 2) + DiagramCard.Wider(shape), Least, config.Widest);
        var deep = Math.Max(config.Tallest, taken.Height + (config.Padded * 2) + DiagramCard.Deeper(shape));

        return new Sized(node, rows, shape, fill, stroke) { Size = new Size(wide, deep) };
    }

    /// <summary>What is written at the top of a boundary: its name, and under it the line saying what it is.</summary>
    private IReadOnlyList<DiagramWords> Naming(C4Bound box, C4Metrics config)
    {
        var ink = Ink.Written(box.Ink) ?? Palette.Text;
        var rows = new List<DiagramWords>();

        if (box.Said is { } said) rows.AddRange(Wrapped(said, null, TextSize, ink, config.Wrapping, FontWeights.SemiBold));

        if (box.Says is { Length: > 0 } says) rows.Add(Worked(says, box.Part, MetaSize, Palette.TextMuted));

        return rows;
    }

    /// <summary>What is written on a relationship: its number where the diagram counts, what it says, and what it is done with.</summary>
    private IReadOnlyList<DiagramWords> Says(C4Link link)
    {
        var ink = Ink.Written(link.SaidInk) ?? Palette.Text;
        var rows = new List<DiagramWords>();

        if (link.Number is { Length: > 0 } number) rows.Add(Worked(number + ".", link.Part, LabelSize, Palette.TextMuted));
        if (link.Said is { Length: > 0 } said) rows.AddRange(Wrapped(said, null, LabelSize, ink, Widest));

        foreach (var under in link.Under)
            rows.Add(Worked("[" + under.Text + "]", under, MetaSize, Palette.TextMuted));

        return rows;
    }

    // ── The relationships ───────────────────────────────────────────────────

    /// <summary>Where every relationship runs once everything is placed, its ends brought in to the cards it joins.</summary>
    private List<Route> Routes(C4Structure diagram, Plan plan, DiagramRoom room)
    {
        var routes = new List<Route>();
        var way = diagram.Way == C4Way.Right ? DiagramWay.Right : DiagramWay.Down;
        var down = way is DiagramWay.Down or DiagramWay.Up;

        // Several lines between the same two things are a couple, and what is written on one of them is held clear of the
        // thing that line leaves. So the two of a pair running opposite ways sit at opposite ends of the gap, each the same
        // distance from its own source — which is what says at a glance which words belong to which line. Lines of a couple
        // running the same way have the same source, and stack one under another beneath it.
        foreach (var couple in diagram.Links.GroupBy(Pairing))
        {
            var running = new[] { DiagramConnector.Clearance, DiagramConnector.Clearance };

            foreach (var link in couple)
            {
                if (!plan.Joins.TryGetValue(link, out var join) || join.Route.Count < 2) continue;

                var placed = DiagramConnector.Trimmed(join).Select(room.At).ToList();
                var said = plan.Said[link];
                var group = Grouped(diagram, link);
                var taken = DiagramConnector.Room(placed, said);
                var deep = down ? taken.Height : taken.Width;
                var side = Forward(link) ? 0 : 1;

                var where = taken.IsEmpty || couple.Count() == 1
                    ? Written(plan, room, link, group, placed, taken, way)
                    : Stacked(placed, taken, running[side] + (deep / 2), down);

                if (!taken.IsEmpty) running[side] += deep + DiagramConnector.Stacking;

                routes.Add(new Route(link, placed, said) { Room = where, Group = group });
            }
        }

        Spread(routes, way);

        return routes;
    }

    /// <summary>
    /// The boundary a relationship is drawn inside: the innermost one holding both of its ends, or none where it runs
    /// between boundaries. What it is drawn inside is what a press on it is inside, and what holds it is sized to hold it.
    /// </summary>
    private static string? Grouped(C4Structure diagram, C4Link link)
    {
        var from = Within(diagram, link.From);
        var to = Within(diagram, link.To);

        return from.LastOrDefault(key => to.Contains(key, StringComparer.Ordinal));
    }

    /// <summary>
    /// Where what is written on a relationship goes: in the gap between the two things the group holding it sees it run
    /// between, which is room that group's own layout kept when it placed them.
    ///
    /// <para>
    /// This is why the drawing nests. A line from a card three boundaries deep to one in another boundary runs through
    /// several boxes and their names on its way, and no point along it is meaningfully "the middle" — but the group that
    /// holds both of its ends sees a line between two of its own children, with the gap the layout left between them to
    /// write in. Hunting along the line for somewhere clear would be answering geometrically a question the tree already
    /// answers.
    /// </para>
    /// </summary>
    private static Rect Written(Plan plan, DiagramRoom room, C4Link link, string? group, IReadOnlyList<Point> along,
                                Rect taken, DiagramWay way)
    {
        if (taken.IsEmpty) return taken;

        var at = Between(plan, link, group) is { } ends
            ? Crossing(along, room.At(ends.From.Bounds), room.At(ends.To.Bounds), way)
            : Middle(along);

        return new Rect(at.X - (taken.Width / 2), at.Y - (taken.Height / 2), taken.Width, taken.Height);
    }

    /// <summary>
    /// What is written on one of a couple, set that far into the gap from the thing its own line leaves. Far enough in that
    /// the head at that end still shows, and the gap was sized for it.
    ///
    /// <para>
    /// <strong>Measured across the gap, not along the line.</strong> A line bowed out and back is half as long again as
    /// the gap it crosses, and it spends the first of that going sideways — so a distance taken along it puts the words
    /// wherever the bow happened to carry them, which is how two of them come to sit on top of one another in a gap that
    /// was sized to hold them apart.
    /// </para>
    /// </summary>
    private static Rect Stacked(IReadOnlyList<Point> along, Rect taken, double into, bool down)
    {
        var (from, to) = down ? (along[0].Y, along[^1].Y) : (along[0].X, along[^1].X);

        var mine = At(along, from + (Math.Sign(to - from) * into), down);

        return new Rect(mine.X - (taken.Width / 2), mine.Y - (taken.Height / 2), taken.Width, taken.Height);
    }

    /// <summary>Which of the two ways round a couple runs a line takes, so the two of them are counted off separately.</summary>
    private static bool Forward(C4Link link) => string.CompareOrdinal(link.From, link.To) <= 0;



    /// <summary>The point that far along a route, as a share of its whole length.</summary>
    private static Point Portion(IReadOnlyList<Point> along, double share)
    {
        var whole = 0d;
        for (var at = 1; at < along.Count; at++) whole += (along[at] - along[at - 1]).Length;

        var run = whole * share;

        for (var at = 1; at < along.Count; at++)
        {
            var step = along[at] - along[at - 1];
            if (step.Length <= 0) continue;
            if (step.Length >= run) return along[at - 1] + (step * (run / step.Length));

            run -= step.Length;
        }

        return along[^1];
    }

    /// <summary>A share along a couple, turned round for the line of it that runs the other way.</summary>
    private static double Shared(C4Link link, double share) =>
        string.CompareOrdinal(link.From, link.To) <= 0 ? share : 1 - share;

    /// <summary>
    /// Which side of its own straight run a line bows to — minus one, nought or one — which is the side its words are set
    /// out towards. Read off the line itself rather than passed down, since the layout is what decided it.
    /// </summary>
    private static double Bowed(IReadOnlyList<Point> along, DiagramWay way)
    {
        var bowed = Portion(along, 0.5);
        var straight = new Point((along[0].X + along[^1].X) / 2, (along[0].Y + along[^1].Y) / 2);
        var off = way is DiagramWay.Down or DiagramWay.Up ? bowed.X - straight.X : bowed.Y - straight.Y;

        return Math.Abs(off) < 0.5 ? 0 : Math.Sign(off);
    }

    /// <summary>
    /// The two things the group holding a relationship sees it run between: an end inside a boundary of that group is that
    /// boundary, since from outside it that is what the line reaches.
    /// </summary>
    private static (DiagramCell From, DiagramCell To)? Between(Plan plan, C4Link link, string? group)
    {
        var holder = group is not null && plan.Boxes.TryGetValue(group, out var box) ? box.Cell : null;

        if (Upto(plan, link.From, holder) is not { } from) return null;
        if (Upto(plan, link.To, holder) is not { } to) return null;

        return ReferenceEquals(from, to) ? null : (from, to);
    }

    /// <summary>An element's cell, or the cell of the boundary holding it that is a child of <paramref name="holder"/>.</summary>
    private static DiagramCell? Upto(Plan plan, string id, DiagramCell? holder)
    {
        if (!plan.Named.TryGetValue(id, out var node)) return null;

        for (var cell = node.Cell; cell is not null; cell = cell.Inside)
            if (ReferenceEquals(cell.Inside, holder)) return cell;

        return node.Cell;
    }

    /// <summary>Where a route crosses the gap between two things the layout placed, or its own middle where they leave none.</summary>
    private static Point Crossing(IReadOnlyList<Point> along, Rect from, Rect to, DiagramWay way)
    {
        var down = way is DiagramWay.Down or DiagramWay.Up;

        var near = down ? Math.Min(from.Bottom, to.Bottom) : Math.Min(from.Right, to.Right);
        var far = down ? Math.Max(from.Top, to.Top) : Math.Max(from.Left, to.Left);

        return far <= near ? Middle(along) : At(along, (near + far) / 2, down);
    }

    /// <summary>The point on a route whose depth across the layout is <paramref name="value"/>.</summary>
    private static Point At(IReadOnlyList<Point> along, double value, bool down)
    {
        for (var at = 1; at < along.Count; at++)
        {
            var (one, other) = (down ? along[at - 1].Y : along[at - 1].X, down ? along[at].Y : along[at].X);
            if (value < Math.Min(one, other) || value > Math.Max(one, other)) continue;

            var share = Math.Abs(other - one) < 0.01 ? 0.5 : (value - one) / (other - one);

            return along[at - 1] + ((along[at] - along[at - 1]) * share);
        }

        return Middle(along);
    }

    /// <summary>The point halfway along a route, by length.</summary>
    private static Point Middle(IReadOnlyList<Point> along)
    {
        var whole = 0d;
        for (var at = 1; at < along.Count; at++) whole += (along[at] - along[at - 1]).Length;

        var half = whole / 2;

        for (var at = 1; at < along.Count; at++)
        {
            var run = along[at] - along[at - 1];
            if (run.Length <= 0) continue;
            if (run.Length >= half) return along[at - 1] + (run * (half / run.Length));

            half -= run.Length;
        }

        return along[^1];
    }

    /// <summary>
    /// Two relationships leaving the same rank write in the same gap, so what they say can land on one another. Where it does,
    /// the words are pushed apart across the layout — a little off their own line, which reads, rather than over each other,
    /// which does not.
    /// </summary>
    private static void Spread(IReadOnlyList<Route> routes, DiagramWay way)
    {
        var down = way is DiagramWay.Down or DiagramWay.Up;

        for (var pass = 0; pass < 8; pass++)
        {
            var moved = false;

            for (var one = 0; one < routes.Count; one++)
                for (var two = one + 1; two < routes.Count; two++)
                {
                    var (first, second) = (routes[one], routes[two]);
                    if (first.Room.IsEmpty || second.Room.IsEmpty) continue;

                    // A couple is staggered along its own lines already, and nudging those would take each off the line it
                    // belongs to. This is for words of different lines that happen to land on one another.
                    if (Pairing(first.Link) == Pairing(second.Link)) continue;

                    var over = Rect.Intersect(Rect.Inflate(first.Room, Apart / 2, Apart / 2), second.Room);
                    if (over.IsEmpty) continue;

                    // Which of them goes back is settled by where each line is heading rather than by where its words have got
                    // to: two lines leaving the same card have not parted yet where they are written on, so going by the words
                    // would as likely put each over the other's line as its own.
                    var step = ((down ? over.Width : over.Height) / 2) + 1;
                    var back = Leaning(first, down) <= Leaning(second, down);

                    first.Room = Shifted(first.Room, down, back ? -step : step);
                    second.Room = Shifted(second.Room, down, back ? step : -step);
                    moved = true;
                }

            if (!moved) return;
        }
    }

    /// <summary>Where a line is heading — the end it runs to, which is what says which side of another line's words its own go.</summary>
    private static double Leaning(Route route, bool sideways) =>
        sideways ? route.Along[^1].X : route.Along[^1].Y;

    /// <summary>The two ends a relationship runs between, whichever way round it runs — two lines between the same pair.</summary>
    private static (string, string) Pairing(C4Link link) =>
        string.CompareOrdinal(link.From, link.To) <= 0 ? (link.From, link.To) : (link.To, link.From);

    private static Rect Shifted(Rect room, bool sideways, double by) =>
        sideways ? Rect.Offset(room, by, 0) : Rect.Offset(room, 0, by);

    /// <summary>The boundaries an element is written inside, outermost first.</summary>
    private static List<string> Within(C4Structure diagram, string id)
    {
        var chain = new List<string>();

        for (var key = diagram.Find(id)?.Box; key is not null;)
        {
            chain.Insert(0, key);
            key = diagram.Boxes.FirstOrDefault(box => string.Equals(box.Key, key, StringComparison.Ordinal))?.Parent;
        }

        return chain;
    }

    /// <summary>The relationships, drawn over the diagram.</summary>
    private void Relations(LayoutBuilder build, IReadOnlyList<Route> routes, string? group)
    {
        var held = routes.Where(route => string.Equals(route.Group, group, StringComparison.Ordinal)).ToList();
        if (held.Count == 0) return;

        build.Open(C4Piece.Relations, part: null, stops: Stops.None);

        // Every line first, then everything written on them — one line drawn after another's words would run over them.
        foreach (var route in held)
        {
            var stroke = new DiagramStroke(Ink.Written(route.Link.Ink) ?? Palette.TextMuted, Thick,
                                           route.Link.Dotted ? DiagramStroke.Dashed : null);

            // A relationship runs from the first end to the second; a BiRel draws a head at each.
            DiagramConnector.Draw(build, C4Piece.Relation, route.Link.Part, route.Along, stroke,
                                  route.Link.Both ? DiagramHead.Arrow : DiagramHead.None, DiagramHead.Arrow, curved: true);
        }

        // Held off the line in a box of its own: what is written on a relationship has a diagram running under it, and an
        // edge is what says where the words stop and the drawing starts again.
        foreach (var route in held)
            DiagramConnector.Says(build, C4Piece.Label, route.Link.Part, route.Room, route.Said, Ink.Surface,
                                  outline: Palette.CodeBorder);

        build.Close();
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>A boundary: its box, its name at the top of it, and everything it holds drawn inside its piece.</summary>
    private void Held(LayoutBuilder build, C4Structure diagram, Plan plan, DiagramRoom room, C4Bound box,
                      IReadOnlyList<Geometry> over, IReadOnlyList<Route> routes)
    {
        var held = plan.Boxes[box.Key];
        var bounds = room.At(held.Cell.Bounds);
        var banner = new Rect(bounds.X, bounds.Y, bounds.Width, held.Cell.Heading);
        var heading = new Rect(banner.X + Boxed, banner.Y + (Boxed / 3), Math.Max(0, banner.Width - (Boxed * 2)),
                               Math.Max(0, banner.Height - (Boxed / 2)));

        // The edge is the band's own colour rather than a stronger one: with the box washed, a loud border is the only
        // thing left shouting. A $borderColor written in the diagram is drawn as written.
        var painted = Ink.Written(box.Fill) ?? C4Grading.Boundary(Palette);
        var edge = Ink.Written(box.Border) ?? DiagramInk.Faded(painted, Banded);

        var outline = DiagramShapes.Outline(DiagramShape.Rounded, bounds);
        var covered = DiagramShapes.United(
        [
            .. over,
            .. diagram.Within(box.Key).Select(inner => DiagramShapes.Outline(DiagramShape.Rounded, room.At(plan.Boxes[inner.Key].Cell.Bounds))),
            .. Inside(diagram, plan, box.Key).Select(node => DiagramShapes.Outline(DiagramShape.Rounded, room.At(node.Cell.Bounds))),
        ]);

        build.Open(C4Piece.Boundary, box.Whole, stops: Stops.None);
        if (box.Href is { Length: > 0 } href) build.Links(href);

        build.Open(C4Piece.Holding, box.Part, stops: Stops.None);

        // A boundary is a surface rather than an outline: a wash over everything it holds says what is inside it at a
        // glance, and a stronger band across the top carries its name. A deployment node is a real box and reads better
        // solid; a logical boundary keeps the dashed edge that says grouping throughout the rest of the diagram family.
        build.Draw(new WashMark(bounds, DiagramInk.Faded(painted, Wash)));
        if (banner.Height > 0) build.Draw(new WashMark(banner, DiagramInk.Faded(painted, Banded)));

        build.Draw(new GeometryMark(outline, null, edge, Thick)
        {
            Dashes = box.Physical ? null : DiagramInk.Dashes("6 4"),
        });

        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, covered);
        stands.Freeze();
        build.Occupies(stands);

        // Its name is written inside the box's own piece, so a press on the band it is written in means the line that
        // names the boundary rather than the whole of what the boundary holds.
        foreach (var (words, at, kind) in Placed(held.Words, heading, box.Says is { Length: > 0 })) words.Set(build, at, kind);

        build.Close();

        foreach (var inner in diagram.Within(box.Key)) Held(build, diagram, plan, room, inner, over, routes);
        foreach (var node in Inside(diagram, plan, box.Key)) Drawn(build, plan, room, node.Node, over);
        Relations(build, routes, box.Key);
        build.Close();
    }

    /// <summary>The words of a boundary's heading, with the line saying what it is marked out as its own piece.</summary>
    private static IReadOnlyList<(DiagramWords Words, Point At, string Kind)> Placed(IReadOnlyList<DiagramWords> words,
                                                                                     Rect heading, bool stereotyped)
    {
        var placed = DiagramWords.Placed(words, heading, MermaidPiece.Words).ToList();

        if (stereotyped && placed.Count > 0)
            placed[^1] = (placed[^1].Words, placed[^1].At, C4Piece.Stereotype);

        return placed;
    }

    /// <summary>One element: its card, and what is written in it.</summary>
    private void Drawn(LayoutBuilder build, Plan plan, DiagramRoom room, C4Node node, IReadOnlyList<Geometry> over)
    {
        if (plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)) is not { } sized) return;

        var bounds = room.At(sized.Cell.Bounds);
        var outline = DiagramCard.Outline(sized.Shape, bounds);
        var placed = DiagramWords.Placed([.. sized.Rows.Select(row => row.Words)],
                                         DiagramCard.Inside(sized.Shape, bounds), MermaidPiece.Words).ToList();

        var covered = new GeometryGroup();
        foreach (var shape in over) covered.Children.Add(shape);
        foreach (var (words, at, _) in placed)
            covered.Children.Add(new RectangleGeometry(new Rect(at, new Size(words.Width, words.Height))));

        build.Open(C4Piece.Element, node.Part, stops: Stops.None);
        if (node.Href is { Length: > 0 } href) build.Links(href);

        build.Open(MermaidPiece.Shape, node.Part, stops: Stops.None);
        build.Draw(new GeometryMark(outline, sized.Fill, sized.Stroke, Thick));

        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, covered);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        for (var at = 0; at < placed.Count; at++)
            placed[at].Words.Set(build, placed[at].At, sized.Rows[at].Kind);

        build.Close();

        Chipped(build, node.Id, bounds, node.Part);
    }

    private static IEnumerable<Sized> Inside(C4Structure diagram, Plan plan, string? box) =>
        diagram.Inside(box)
            .Select(node => plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)))
            .OfType<Sized>();

    /// <summary>
    /// The key, where <c>SHOW_LEGEND()</c> asked for one: a row per kind of element written. It is handed to the block to
    /// put under the drawing rather than drawn into the drawing, so the two are set about the same middle whichever of them
    /// turns out to be the wider.
    /// </summary>
    private void Keyed(C4Structure diagram)
    {
        if (diagram.Legend.Count == 0) return;

        // A row's swatch is the colour the cards it explains are actually drawn in: what is written for it, and otherwise
        // the band of the grading they take. A row explaining a tag rather than a kind has only what the tag wrote.
        var rows = diagram.Legend
            .Select(row => new DiagramKey(null,
                                          Ink.Written(row.Fill) ?? Ink.Written(row.Border)
                                          ?? (row.Tone is { } tone ? this.ink.Band(tone) : null),
                                          [Worked(row.Says, null, LabelSize, Palette.Text)]))
            .ToList();

        var key = new DiagramLegend(rows, [MermaidPiece.Words], across: true, Palette.CodeBorder)
        {
            Framed = (DiagramInk.Faded(Palette.Text, 0.04), Palette.CodeBorder),
        };

        // With the air either side of it that diagramMarginX asks for, kept as part of the piece so the block is as wide
        // as the legend plus its margins rather than the legend alone.
        var build = new LayoutBuilder();
        key.Draw(build, new Point(diagram.Config.Across, 0));

        Beneath(build.Seal(), new Size(key.Size.Width + (diagram.Config.Across * 2), key.Size.Height), Clear);
    }

    private static DiagramCardShape Carded(C4Shape shape) => shape switch
    {
        C4Shape.Database => DiagramCardShape.Database,
        C4Shape.Queue => DiagramCardShape.Queue,
        C4Shape.Person => DiagramCardShape.Person,
        _ => DiagramCardShape.Box,
    };

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>A card measured: the words of each row, the outline they are set in, and the cell the layout placed it in.</summary>
    private sealed class Sized(C4Node node, IReadOnlyList<(DiagramWords Words, string Kind)> rows, DiagramCardShape shape,
                               Brush fill, Brush stroke)
    {
        public C4Node Node { get; } = node;

        public IReadOnlyList<(DiagramWords Words, string Kind)> Rows { get; } = rows;

        public DiagramCardShape Shape { get; } = shape;

        public Brush Fill { get; } = fill;

        public Brush Stroke { get; } = stroke;

        public Size Size { get; init; }

        public DiagramCell Cell { get; set; } = new(default);
    }

    /// <summary>A boundary measured: what is written at the top of it, and the cell the layout placed it in.</summary>
    private sealed class Bound(C4Bound box, IReadOnlyList<DiagramWords> words)
    {
        public C4Bound Box { get; } = box;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>Where a relationship runs, what is written on it, and the room those words take.</summary>
    private sealed record Route(C4Link Link, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said)
    {
        /// <summary>Where what is written on it goes, which is settled once every route is known (<see cref="Spread"/>).</summary>
        public Rect Room { get; set; }

        /// <summary>The boundary it is drawn inside, or null for one drawn outside them all.</summary>
        public string? Group { get; init; }
    }

    /// <summary>Everything placed.</summary>
    private sealed class Plan
    {
        public List<Sized> Nodes { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Bound> Boxes { get; } = new(StringComparer.Ordinal);

        public Dictionary<C4Link, DiagramJoin> Joins { get; } = [];

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public DiagramSpill Spill { get; set; } = DiagramSpill.None;

        /// <summary>What is written on each relationship, measured before anything is placed — the ranks are held apart for it.</summary>
        public Dictionary<C4Link, IReadOnlyList<DiagramWords>> Said { get; } = [];

        /// <summary>How far apart the ranks were held, which is the gap each relationship's words are set in.</summary>
        public double Along { get; set; }

        public Size Size { get; set; }
    }
}
