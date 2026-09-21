using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Cynefin;

/// <summary>The pieces a Cynefin diagram's layout is made of — its layers, and what is in them.</summary>
public static class CynefinPiece
{
    /// <summary>The four practised domains, and one — standing for the line opening it.</summary>
    public const string Domains = "Domains";
    public const string Domain = "Domain";

    /// <summary>The line round the grid, and the boundaries sweeping from its edges into the disorder in the middle.</summary>
    public const string Boundaries = "Boundaries";

    /// <summary>The movements between domains, and one — standing for the transition as written.</summary>
    public const string Moves = "Moves";
    public const string Move = "Move";

    /// <summary>Disorder: the confusion domain, drawn as a cloud in the middle with what is in it inside.</summary>
    public const string Centre = "Centre";

    /// <summary>The items, and one — standing for the item as written.</summary>
    public const string Items = "Items";
    public const string Item = "Item";

    /// <summary>What is written on the grid: each domain's word, how it is worked, an item's words, a movement's label.</summary>
    public const string Words = "Words";
    public const string Name = "Name";
    public const string About = "About";
    public const string Says = "Says";
    public const string Label = "Label";
}

/// <summary>
/// Draws a <c>cynefin-beta</c> block: the four practised domains in the corners of a grid, disorder as a cloud in the middle,
/// each domain's items carded in its outer corner, and a movement drawn as a dashed arrow from one domain to another.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A domain stands for the line opening it, a card for its
/// item's line, the cloud for the <c>confusion</c> line, an arrow for its transition — and every word is the characters
/// written, typed into where it is drawn. The grid grows to hold what is in it rather than trimming any of it away, so a
/// domain's items are all drawn; the four boundaries sweep the same way round, which is the swirl a Cynefin diagram is
/// drawn with, and a movement that would cross the middle bends round the disorder instead.
/// </para>
/// </summary>
internal sealed class CynefinBuilder : MermaidBuilder<CynefinDiagram>
{
    /// <summary>The least a domain's cell is drawn at, before what is in it asks for more.</summary>
    private const double Least = 210;

    private const double Pad = 12;
    private const double Gap = 6;
    private const double Snug = 4;

    /// <summary>The clear air round the words in a card, and between a movement's line and its label.</summary>
    private const double Inset = 6;
    private const double Beside = 3;

    /// <summary>How wide an item's words are set before they wrap, and the least they are squeezed to in a narrow column.</summary>
    private const double Widest = 230;
    private const double Middling = 160;
    private const double Narrowest = 90;

    private const double NameSize = 13;
    private const double AboutSize = 10;
    private const double ItemSize = 11;
    private const double LabelSize = 10;

    /// <summary>How solid a domain is tinted where no front matter colours it.</summary>
    private const double Tint = 0.16;

    /// <summary>How far a boundary bows as it sweeps in, how thick a movement is drawn, and how heavy the cliff is.</summary>
    private const double Bow = 30;
    private const double Thick = 1.5;
    private const double Falling = 2.5;

    /// <summary>How far along a movement its label sits, and how far its bend clears the disorder it goes round.</summary>
    private const double Along = 0.3;
    private const double Round = 16;

    /// <summary>Which corner each domain is drawn in, and the series colour it is tinted with.</summary>
    private static readonly IReadOnlyList<(CynefinDomain Domain, bool Left, bool Top)> Corners =
    [
        (CynefinDomain.Complex, true, true),
        (CynefinDomain.Complicated, false, true),
        (CynefinDomain.Chaotic, true, false),
        (CynefinDomain.Clear, false, false),
    ];

    private CynefinBuilder(ContentReading reading, DiagramLaying laying) : base(reading, laying) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(ContentReading reading, DiagramLaying laying) => new CynefinBuilder(reading, laying).Lay();

    /// <inheritdoc/>
    protected override CynefinDiagram Of(MermaidBlock block) => CynefinDiagram.Of(block);

    protected override Size Draw(CynefinDiagram diagram, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source.
        if (diagram.Empty) return AsWritten(build);

        var config = diagram.Config;
        var pad = config.Padding ?? Pad;
        var wrap = double.IsInfinity(Space) ? Widest : Math.Clamp((Space / 2) - (pad * 2) - Round, Narrowest, Widest);

        var stacks = Corners.ToDictionary(corner => corner.Domain, corner => Stacked(diagram, corner.Domain, wrap));
        var disorder = Lines(Stacked(diagram, CynefinDomain.Confusion, Math.Min(wrap, Middling)));

        // The cloud in the middle is as big as what is in it, and every cell keeps clear of the half of it reaching into that corner.
        var cloud = disorder.Count == 0
            ? default
            : DiagramShapes.Around(DiagramShape.Cloud, new Size(disorder.Max(line => line.Words.Width), disorder.Sum(line => line.Words.Height)), Snug * 2);

        var wide = Math.Max(Math.Max(Least, (config.Width ?? 0) / 2), stacks.Values.Max(stack => stack.Taken.Width) + (pad * 2) + (cloud.Width / 2));
        var tall = Math.Max(Math.Max(Least, (config.Height ?? 0) / 2), stacks.Values.Max(stack => stack.Taken.Height) + (pad * 2) + (cloud.Height / 2));

        var plot = new Rect(0, 0, wide * 2, tall * 2);
        var middle = new Point(wide, tall);
        var blob = new Rect(middle.X - (cloud.Width / 2), middle.Y - (cloud.Height / 2), cloud.Width, cloud.Height);

        var cells = new Dictionary<CynefinDomain, Rect>();
        var words = new List<(DiagramWords Words, Point At, string Kind)>();
        var cards = new List<(Card Card, Rect Bounds, bool Left)>();

        // Each domain's words and cards stack from its outer corner inwards, which is what keeps them clear of the middle.
        foreach (var (domain, left, top) in Corners)
        {
            var cell = new Rect(left ? plot.Left : middle.X, top ? plot.Top : middle.Y, wide, tall);
            cells[domain] = cell;

            var box = Rect.Inflate(cell, -pad, -pad);
            var stack = stacks[domain];
            var y = top ? box.Top : box.Bottom;

            Point Place(Size size, double after)
            {
                var at = new Point(left ? box.Left : box.Right - size.Width, top ? y : y - size.Height);
                y += top ? size.Height + after : -(size.Height + after);
                return at;
            }

            if (stack.Name is { } name) words.Add((name, Place(new Size(name.Width, name.Height), stack.About.Count == 0 ? Gap : Snug), CynefinPiece.Name));

            // Stacking up from the foot of the grid puts the last line first, so what it says stays in the order it is read in.
            var said = top ? stack.About : [.. stack.About.Reverse()];

            for (var line = 0; line < said.Count; line++)
            {
                var about = said[line];
                words.Add((about, Place(new Size(about.Width, about.Height), line == said.Count - 1 ? Gap : 0), CynefinPiece.About));
            }
            foreach (var card in stack.Cards) cards.Add((card, new Rect(Place(card.Size, Gap), card.Size), left));
        }

        var routes = Routes(diagram, cells, middle, blob, cloud, words);

        var room = new DiagramRoom();
        room.Reach(plot);
        foreach (var (said, at, _) in words) room.Reach(said, at);
        foreach (var (_, bounds, _) in cards) room.Reach(bounds);
        if (cloud.Width > 0) room.Reach(blob);
        foreach (var (_, route) in routes) room.Reach(route[0], route[^1]);

        var shift = room.Shift;

        // What is drawn over the domains, which a press there means rather than the domain under it.
        var over = new GeometryGroup();
        foreach (var (said, at, _) in words) over.Children.Add(new RectangleGeometry(new Rect(at + shift, new Size(said.Width, said.Height))));
        foreach (var (_, bounds, _) in cards) over.Children.Add(new RectangleGeometry(Rect.Offset(bounds, shift)));
        if (cloud.Width > 0) over.Children.Add(DiagramShapes.Outline(DiagramShape.Cloud, Rect.Offset(blob, shift)));
        foreach (var (_, route) in routes) over.Children.Add(DiagramConnector.Band([.. route.Select(at => at + shift)], Thick, curved: route.Count > 2));
        over.Freeze();

        Domains(build, diagram, cells, shift, over);
        Boundaries(build, config, plot, blob, cloud, shift);
        Moves(build, config, routes, shift);
        Centre(build, diagram, disorder, blob, cloud, shift);
        Cards(build, cards, shift);

        build.Open(CynefinPiece.Words, part: null, stops: Stops.None);
        foreach (var (said, at, kind) in words) said.Set(build, at + shift, kind);
        build.Close();

        return room.Size;
    }

    // ── What a domain shows ─────────────────────────────────────────────────

    /// <summary>One item, its words wrapped, and the card drawn round them.</summary>
    private sealed record Card(CynefinItem Item, IReadOnlyList<DiagramWords> Lines, Size Size);

    /// <summary>What a domain shows, stacked from its outer corner: its word, how it is worked, and a card for each item in it.</summary>
    private sealed record Shown(DiagramWords? Name, IReadOnlyList<DiagramWords> About, IReadOnlyList<Card> Cards)
    {
        /// <summary>How much room it all takes, the gaps between included.</summary>
        public Size Taken => new(
            Math.Max(Math.Max(Name?.Width ?? 0, DiagramWords.Taken(About).Width), Cards.Select(card => card.Size.Width).DefaultIfEmpty(0).Max()),
            (Name?.Height ?? 0) + (About.Count == 0 ? 0 : DiagramWords.Taken(About).Height + Snug) + Cards.Sum(card => card.Size.Height + Gap));
    }

    private Shown Stacked(CynefinDiagram diagram, CynefinDomain domain, double wrap)
    {
        var config = diagram.Config;
        var items = diagram.ItemsIn(domain);
        var opened = diagram.Opened(domain);

        var ink = Ink.Written(config.TextColour) ?? Palette.Text;
        var said = config.ItemFontSize ?? ItemSize;

        var name = opened is null
            ? null
            : Written(opened.Word, hole: null, config.DomainFontSize ?? NameSize, Ink.Written(config.LabelColour) ?? Palette.Text, FontWeights.SemiBold);

        // How the domain is worked, in the words the framework uses for it: the decision model, then the practice.
        var about = config.ShowDomainDescriptions && (opened is not null || items.Count > 0)
            ? CynefinDiagram.Practice(domain)
                .Select(says => Worked(says, opened?.Part, config.ItemFontSize ?? AboutSize, Ink.Written(config.TextColour) ?? Palette.TextMuted))
                .ToList()
            : [];

        var cards = items.Select(item =>
        {
            var lines = Says(item.Says.Says, item.Says.Hole, said, ink, wrap);
            return new Card(item, lines, DiagramShapes.Around(DiagramShape.Rounded, DiagramWords.Taken(lines), Inset));
        });

        return new Shown(name, about, [.. cards]);
    }

    /// <summary>What disorder shows, line by line: its word, how it is worked, and what each item in it says.</summary>
    private static IReadOnlyList<(DiagramWords Words, string Kind)> Lines(Shown shown)
    {
        var lines = new List<(DiagramWords Words, string Kind)>();

        if (shown.Name is { } name) lines.Add((name, CynefinPiece.Name));
        lines.AddRange(shown.About.Select(about => (about, CynefinPiece.About)));
        lines.AddRange(shown.Cards.SelectMany(card => card.Lines).Select(line => (line, CynefinPiece.Says)));

        return lines;
    }

    // ── Where a movement runs ───────────────────────────────────────────────

    /// <summary>
    /// Each movement's route: between the middles of the two domains' cells, to the edge of the cloud where it ends in
    /// disorder, and bent round the cloud where it would otherwise cross it. Its label goes on the list of words as it goes.
    /// </summary>
    private IReadOnlyList<(CynefinMove Move, IReadOnlyList<Point> Route)> Routes(CynefinDiagram diagram, IReadOnlyDictionary<CynefinDomain, Rect> cells,
        Point middle, Rect blob, Size cloud, List<(DiagramWords Words, Point At, string Kind)> words)
    {
        var routes = new List<(CynefinMove Move, IReadOnlyList<Point> Route)>();

        foreach (var move in diagram.Moves)
        {
            if (move.To is not { } to || to == move.From) continue;

            var from = Anchor(move.From, Anchor(to, middle));
            var at = Anchor(to, from);
            var route = Bent(from, at);

            routes.Add((move, route));

            if (Said(move.Label, LabelSize, Ink.Written(diagram.Config.TextColour) ?? Palette.Text) is { } label)
            {
                // Beside the line rather than under it, square to the way it runs, so the line never crosses what it says.
                var along = route[1] - from;
                along.Normalize();

                var off = new Vector(along.Y, -along.X) * ((label.Height / 2) + Beside);
                var beside = from + ((route[1] - from) * Along) + off;

                words.Add((label, new Point(beside.X - (label.Width / 2), beside.Y - (label.Height / 2)), CynefinPiece.Label));
            }
        }

        return routes;

        Point Anchor(CynefinDomain domain, Point toward) =>
            cells.TryGetValue(domain, out var cell) ? new Point(cell.Left + (cell.Width / 2), cell.Top + (cell.Height / 2))
            : cloud.Width > 0 ? DiagramShapes.Edge(DiagramShape.Cloud, blob, toward)
            : middle;

        // A movement across the grid would run under the cloud, where nothing of it would be seen: it bends round instead.
        IReadOnlyList<Point> Bent(Point from, Point at)
        {
            var clear = Math.Max(cloud.Width, cloud.Height) / 2;
            if (clear <= 0) return [from, at];

            var half = new Point((from.X + at.X) / 2, (from.Y + at.Y) / 2);
            if ((half - middle).Length > clear) return [from, at];

            var along = at - from;
            along.Normalize();

            return [from, half + (new Vector(along.Y, -along.X) * (clear + Round)), at];
        }
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    private void Domains(LayoutBuilder build, CynefinDiagram diagram, IReadOnlyDictionary<CynefinDomain, Rect> cells, Vector shift, Geometry over)
    {
        build.Open(CynefinPiece.Domains, part: null, stops: Stops.None);

        foreach (var (domain, _, _) in Corners)
        {
            var shape = new RectangleGeometry(Rect.Offset(cells[domain], shift));
            shape.Freeze();

            build.Open(CynefinPiece.Domain, diagram.Opened(domain)?.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, Fill(diagram, domain), null, 0));

            // A domain stands where nothing drawn over it does: a press on a card, a word or a movement means that.
            if (diagram.Opened(domain) is not null)
            {
                var stands = new CombinedGeometry(GeometryCombineMode.Exclude, shape, over);
                stands.Freeze();
                build.Occupies(stands);
            }

            build.Close();
        }

        build.Close();
    }

    /// <summary>
    /// The line round the grid, and the four boundaries sweeping from the middle of each edge into the disorder — the one from
    /// the foot of the grid being the cliff, the fall from clear into chaotic, which Cynefin draws heavier than the rest.
    /// </summary>
    private void Boundaries(LayoutBuilder build, CynefinConfig config, Rect plot, Rect blob, Size cloud, Vector shift)
    {
        var grid = Rect.Offset(plot, shift);
        var middle = new Point(grid.Left + (grid.Width / 2), grid.Top + (grid.Height / 2));
        var disorder = Rect.Offset(blob, shift);

        var lines = new GeometryGroup { Children = { new RectangleGeometry(grid) } };
        var cliff = new GeometryGroup();

        foreach (var (edge, falls) in new[]
                 {
                     (new Point(middle.X, grid.Top), false),
                     (new Point(grid.Right, middle.Y), false),
                     (new Point(middle.X, grid.Bottom), true),
                     (new Point(grid.Left, middle.Y), false),
                 })
        {
            var into = cloud.Width > 0 ? DiagramShapes.Edge(DiagramShape.Cloud, disorder, edge) : middle;
            var half = new Point((edge.X + into.X) / 2, (edge.Y + into.Y) / 2);

            // Bowed square to the way it runs, every boundary the same way round: the swirl.
            var along = into - edge;
            along.Normalize();

            (falls ? cliff : lines).Children.Add(DiagramCurve.Bowed(edge, half + (new Vector(along.Y, -along.X) * Bow), into));
        }

        lines.Freeze();
        cliff.Freeze();

        build.Open(CynefinPiece.Boundaries, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(lines, null, Ink.Written(config.BoundaryColour) ?? Palette.CodeBorder, config.BoundaryWidth ?? 1));
        build.Draw(new GeometryMark(cliff, null, Ink.Written(config.CliffColour) ?? Palette.Danger, config.CliffWidth ?? Falling));
        build.Close();
    }

    private void Moves(LayoutBuilder build, CynefinConfig config, IReadOnlyList<(CynefinMove Move, IReadOnlyList<Point> Route)> routes, Vector shift)
    {
        if (routes.Count == 0) return;

        var stroke = new DiagramStroke(Ink.Written(config.ArrowColour) ?? Palette.TextMuted, config.ArrowWidth ?? Thick, DiagramStroke.Dashed);

        build.Open(CynefinPiece.Moves, part: null, stops: Stops.None);
        foreach (var (move, route) in routes)
            DiagramConnector.Draw(build, CynefinPiece.Move, move.Part, [.. route.Select(at => at + shift)], stroke, curved: route.Count > 2);
        build.Close();
    }

    /// <summary>Disorder: a cloud in the middle, with its word and what is in it inside.</summary>
    private void Centre(LayoutBuilder build, CynefinDiagram diagram, IReadOnlyList<(DiagramWords Words, string Kind)> disorder,
                        Rect blob, Size cloud, Vector shift)
    {
        if (cloud.Width <= 0) return;

        var bounds = Rect.Offset(blob, shift);
        var placed = DiagramWords.Stack([.. disorder.Select(line => line.Words)], DiagramShapes.Inside(DiagramShape.Cloud, bounds)).ToList();
        var words = placed.Select((line, at) => (line.Words, line.At, disorder[at].Kind)).ToList();

        DiagramShapes.Draw(build, CynefinPiece.Centre, diagram.Opened(CynefinDomain.Confusion)?.Part, DiagramShape.Cloud, bounds,
            Fill(diagram, CynefinDomain.Confusion), new DiagramStroke(Ink.Written(diagram.Config.BoundaryColour) ?? Palette.CodeBorder), words);
    }

    private void Cards(LayoutBuilder build, IReadOnlyList<(Card Card, Rect Bounds, bool Left)> cards, Vector shift)
    {
        if (cards.Count == 0) return;

        var stroke = new DiagramStroke(Palette.CodeBorder);

        build.Open(CynefinPiece.Items, part: null, stops: Stops.None);

        foreach (var (card, bounds, left) in cards)
        {
            var at = Rect.Offset(bounds, shift);
            var inside = Rect.Inflate(DiagramShapes.Inside(DiagramShape.Rounded, at), -Inset, -Inset);
            var words = DiagramWords.Placed(card.Lines, inside, CynefinPiece.Says, left ? TextAlignment.Left : TextAlignment.Right);

            DiagramShapes.Draw(build, CynefinPiece.Item, card.Item.Part, DiagramShape.Rounded, at, Palette.CodeBg, stroke, words);
        }

        build.Close();
    }

    // ── Ink ─────────────────────────────────────────────────────────────────

    /// <summary>What a domain is drawn in: the colour its front matter writes, or a faded series colour of its own.</summary>
    private Brush Fill(CynefinDiagram diagram, CynefinDomain domain) =>
        Ink.Written(diagram.Config.DomainFills[(int)domain]) ?? DiagramInk.Faded(Ink.Series(Series(domain)), Tint);

    /// <summary>The series colour each domain takes, a hue apart: disorder pink, and the practised domains round it.</summary>
    private static int Series(CynefinDomain domain) => domain switch
    {
        CynefinDomain.Complex => 4,
        CynefinDomain.Complicated => 0,
        CynefinDomain.Clear => 2,
        CynefinDomain.Chaotic => 1,
        _ => 7,
    };

    private DiagramWords? Said(CynefinText? text, double size, Brush ink) =>
        text is null ? null : Written(text.Says, text.Hole, size, ink);
}
