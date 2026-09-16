using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The pieces a Venn diagram's layout is made of — its layers, and what is in them.</summary>
public static class VennPiece
{
    /// <summary>The circles, which is the diagram itself.</summary>
    public const string Circles = "Circles";

    /// <summary>One set's circle. It stands for the set as it was written — its line and its items — so pressing it means that.</summary>
    public const string Circle = "Circle";

    /// <summary>The overlaps the unions name.</summary>
    public const string Overlaps = "Overlaps";

    /// <summary>
    /// One union's overlap: where its circles meet and no other circle covers, which is what a press on it means — and what
    /// the circles themselves stand back from. Drawn over the whole lens, where the union's style gives it a fill or an outline.
    /// </summary>
    public const string Overlap = "Overlap";

    /// <summary>What is written on the circles: each region's label and the items in it.</summary>
    public const string Labels = "Labels";

    /// <summary>One region's words: its label, and its items under it.</summary>
    public const string Region = "Region";

    /// <summary>A set's or a union's label — or its name, where it has none.</summary>
    public const string Label = "Label";

    /// <summary>One item, holding what it says.</summary>
    public const string Item = "Item";

    /// <summary>What an item says: its label, or its name where it has none.</summary>
    public const string Text = "Text";

    /// <summary>The layout's workings, drawn where the front matter asks for them: each circle's centre, and where each region's words are set.</summary>
    public const string Debug = "Debug";
}

/// <summary>
/// Draws a <c>venn-beta</c> block: a circle per set, each union's overlap, and what is written in each region.
///
/// <para>
/// <strong>The layout is how it looks, not how it was written.</strong> A set is one region in the syntax tree — its line
/// and the items under it — and three things on the page: a circle in the layer of circles, a label, and a word for each
/// item in the layer of words. What belongs together is said by the source they all point at, so choosing the circle
/// chooses the set's label and items too, and the label is the characters of the label and is typed into.
/// </para>
/// <para>
/// <strong>Each region stands in its own shape.</strong> Circles overlap, so a press in the lens between two could mean
/// either. A union's overlap is a piece of its own, standing where its circles meet and no other covers, and each circle
/// stands in what is left of it once the overlaps of the unions it is in are taken out — so a press in a lens means the
/// union, a press anywhere else in a circle means its set, and where no union is written a press in a lens means a circle.
/// The words are a layer above both.
/// </para>
/// </summary>
internal sealed class VennBuilder : MermaidBuilder
{
    /// <summary>How wide the drawing is before anything asks for another width.</summary>
    private const double Wide = 460;

    /// <summary>How tall the drawing is before anything asks for another height.</summary>
    private const double Tall = 300;

    /// <summary>How solid a circle is filled where no style says: Mermaid's tenth is too faint to read against a page.</summary>
    private const double FillOpacity = 0.28;

    private const double StrokeWidth = 2;

    private const double SetSize = 13;
    private const double UnionSize = 12;
    private const double ItemSize = 11;

    /// <summary>Clear air under a label, and between items.</summary>
    private const double LabelGap = 4;
    private const double ItemGap = 2;
    private const double ItemApart = 10;

    private VennDiagram? _diagram;

    private VennBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    /// <param name="writing">Whether somebody is writing in it, which draws what is still to be written.</param>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new VennBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <summary>The same, for a block that has no caret in it.</summary>
    public static Laid Build(string source, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        Build(EditState.For(source), palette, pixelsPerDip, room, writing);

    public static Editing.ContentElement Element(string source, DiagramRenderOptions options) =>
        Host(source, options, Build, readOnly: false);

    /// <inheritdoc/>
    protected override ContentNode Reading(string source) => VennPipeline.Read(source, holes: Writing);

    /// <summary>The diagram's own title where it has one, and the front matter's otherwise.</summary>
    protected override (ContentPart? Part, string? Text) TitleOf(MermaidBlock block) =>
        _diagram is null ? base.TitleOf(block) : (_diagram.Title, _diagram.TitleText);

    /// <summary>The front matter's <c>vennTitleTextColor</c>, where it writes one.</summary>
    protected override Brush TitleInk => Colour(_diagram?.Config.TitleTextColour) ?? base.TitleInk;

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = _diagram = VennDiagram.Of(block);

        // A diagram of no sets is the source: there is nothing to look at, and what the reader wants is their own lines
        // back with whatever is wrong with them said underneath.
        if (diagram.Sets.Count == 0) return AsWritten(build);

        var config = diagram.Config;
        var sets = diagram.Sets;
        var layout = VennLayout.Place(
            [.. sets.Select(set => set.Weight)],
            (one, other) => diagram.Overlap(sets[one], sets[other]),
            [.. diagram.Unions.Where(union => union.Sets.Count >= 3)
                .Select(union => ((IReadOnlyList<int>)[.. union.Sets.Select(id => Order(sets, id))], union.Weight))]);

        var circles = Fitted(layout, config);
        var words = sets.Select((set, at) => Measured(set, circles, [at]))
            .Concat(diagram.Unions.Select(union => Measured(union, circles, [.. union.Sets.Select(id => Order(sets, id))])))
            .ToList();

        // Words set wider than a circle may reach past the drawing's edge: everything moves over rather than being cut off.
        var reached = circles.Select(circle => circle.Bounds).Concat(words.Select(word => word.Bounds)).Aggregate(Rect.Union);
        var shift = new Vector(Math.Max(0, -reached.X), Math.Max(0, -reached.Y));

        var overlaps = diagram.Unions.Select(union => Lensed(union, sets, circles, shift)).OfType<Lens>().ToList();

        Circles(build, sets, circles, overlaps, shift);
        Overlaps(build, overlaps);
        Labels(build, words, shift);
        if (config.UseDebugLayout) Debug(build, circles, words, shift);

        var size = new Size(reached.Right + shift.X + config.Padding, reached.Bottom + shift.Y + config.Padding);
        return new Size(Math.Max(size.Width, config.Width ?? 0), Math.Max(size.Height, config.Height ?? 0));
    }

    // ── Where it goes ───────────────────────────────────────────────────────

    /// <summary>
    /// The circles at the size they are drawn: as big as fits the drawing's width and height less its padding, and that
    /// width no wider than the room there is, unless the front matter asks for the drawing not to be fitted. The drawing is
    /// then as big as the circles, or as the width and height the front matter asks for with the circles in the middle.
    /// </summary>
    private VennLayout.Circle[] Fitted(IReadOnlyList<VennLayout.Circle> layout, VennConfig config)
    {
        var wide = config.Width ?? Wide;
        var tall = config.Height ?? Tall;

        if (config.UseMaxWidth && !double.IsInfinity(Space) && wide > Space)
        {
            tall *= Space / wide;
            wide = Space;
        }

        var extent = layout.Select(circle => circle.Bounds).Aggregate(Rect.Union);
        var scale = Math.Min(Math.Max(1, wide - (config.Padding * 2)) / Math.Max(extent.Width, 1e-9),
                             Math.Max(1, tall - (config.Padding * 2)) / Math.Max(extent.Height, 1e-9));

        // Centred in the width and height asked for; as wide and as tall as the circles come out, where none was.
        var left = config.Padding + (config.Width is null ? 0 : Math.Max(0, (wide - (config.Padding * 2) - (extent.Width * scale)) / 2));
        var top = config.Padding + (config.Height is null ? 0 : Math.Max(0, (tall - (config.Padding * 2) - (extent.Height * scale)) / 2));

        return [.. layout.Select(circle => new VennLayout.Circle(
            new Point(left + ((circle.Centre.X - extent.X) * scale), top + ((circle.Centre.Y - extent.Y) * scale)),
            circle.Radius * scale))];
    }

    private static int Order(IReadOnlyList<VennSet> sets, string id)
    {
        for (var at = 0; at < sets.Count; at++)
            if (sets[at].Id == id) return at;

        return -1;
    }

    // ── The circles ─────────────────────────────────────────────────────────

    private void Circles(LayoutBuilder build, IReadOnlyList<VennSet> sets, IReadOnlyList<VennLayout.Circle> circles,
                         IReadOnlyList<Lens> overlaps, Vector shift)
    {
        build.Open(VennPiece.Circles, part: null, stops: Stops.None);

        for (var at = 0; at < sets.Count; at++)
        {
            var set = sets[at];
            var shape = Disc(circles[at], shift);
            shape.Freeze();

            // What a press on the circle means: all of it but the overlaps its unions stand in.
            var stands = shape;
            foreach (var overlap in overlaps.Where(overlap => overlap.Members.Contains(at)))
                stands = new CombinedGeometry(GeometryCombineMode.Exclude, stands, overlap.Own);
            stands.Freeze();

            var ink = Ink(set);

            build.Open(VennPiece.Circle, set.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, Faded(ink, set.Style.FillOpacity ?? FillOpacity),
                                        Colour(set.Style.Stroke) ?? ink, set.Style.StrokeWidth ?? StrokeWidth));
            build.Occupies(stands);
            build.Close();
        }

        build.Close();
    }

    /// <summary>What a set is drawn in: its style's fill, the front matter's colour for its place, or the theme's next series colour.</summary>
    private Brush Ink(VennSet set) =>
        Colour(set.Style.Fill) ?? Colour(set.Colour) ?? Palette.Series[set.Order % Palette.Series.Count];

    // ── The overlaps ────────────────────────────────────────────────────────

    /// <summary>A union's overlap: the lens its circles make, and the part of it no other circle covers.</summary>
    private sealed record Lens(VennUnion Union, IReadOnlySet<int> Members, Geometry Whole, Geometry Own);

    /// <summary>Where a union's circles meet — or null where they do not.</summary>
    private static Lens? Lensed(VennUnion union, IReadOnlyList<VennSet> sets, IReadOnlyList<VennLayout.Circle> circles, Vector shift)
    {
        var members = union.Sets.Select(id => Order(sets, id)).ToHashSet();

        Geometry whole = Disc(circles[members.First()], shift);
        foreach (var member in members.Skip(1))
            whole = new CombinedGeometry(GeometryCombineMode.Intersect, whole, Disc(circles[member], shift));

        Geometry own = whole;
        for (var at = 0; at < circles.Count; at++)
            if (!members.Contains(at))
                own = new CombinedGeometry(GeometryCombineMode.Exclude, own, Disc(circles[at], shift));

        whole.Freeze();
        own.Freeze();

        return whole.Bounds.IsEmpty ? null : new Lens(union, members, whole, own);
    }

    private void Overlaps(LayoutBuilder build, IReadOnlyList<Lens> overlaps)
    {
        build.Open(VennPiece.Overlaps, part: null, stops: Stops.None);

        foreach (var (union, _, whole, own) in overlaps)
        {
            build.Open(VennPiece.Overlap, union.Part, stops: Stops.None);

            var fill = Colour(union.Style.Fill);
            var stroke = Colour(union.Style.Stroke);
            if (fill is not null || stroke is not null)
                build.Draw(new GeometryMark(whole, fill is null ? null : Faded(fill, union.Style.FillOpacity ?? FillOpacity), stroke,
                                            stroke is null ? 0 : union.Style.StrokeWidth ?? StrokeWidth));

            build.Covers(whole.Bounds);
            build.Occupies(own);
            build.Close();
        }

        build.Close();
    }

    private static Geometry Disc(VennLayout.Circle circle, Vector shift) =>
        new EllipseGeometry(circle.Centre + shift, circle.Radius, circle.Radius);

    // ── What is written on them ─────────────────────────────────────────────

    /// <summary>One thing set in a region: its words, or the hole standing where they go.</summary>
    /// <param name="Part">What was written, where the words are what was written — null for words worked out.</param>
    private sealed record Written(FormattedText Text, ContentPart? Part, ContentPart? Hole, FormattedText Letter, Brush Ink)
    {
        public double Width => Hole is null ? Text.Width : LayoutText.HoleWidth(Letter);

        /// <summary>Whether the words are the characters written, one for one — not an entity code shown as what it stands for.</summary>
        public bool Maps => Part is not null && Text.Text == Part.Text;

        public double Height => Math.Max(Text.Height, Letter.Height);
    }

    /// <summary>One item, measured.</summary>
    private sealed record Entry(VennItem Item, Written Says);

    /// <summary>A region's words, measured and placed: its label on top, and its items in a grid under it.</summary>
    /// <param name="Room">How far the point they are centred on is from the region's nearest edge, or nought where the region has no room.</param>
    /// <param name="Columns">How many columns the items are set in.</param>
    private sealed record Words(VennRegion Region, Written? Label, IReadOnlyList<Entry> Items, Point Centre, double Room, int Columns = 1)
    {

        public int Rows => Items.Count == 0 ? 0 : (int)Math.Ceiling(Items.Count / (double)Columns);

        public double Cell => Items.Count == 0 ? 0 : Items.Max(entry => entry.Says.Width);

        public double Line => Items.Count == 0 ? 0 : Items.Max(entry => entry.Says.Height);

        public double Width => Math.Max(Label?.Width ?? 0, Items.Count == 0 ? 0 : (Columns * Cell) + ((Columns - 1) * ItemApart));

        public double Height => (Label?.Height ?? 0) + (Label is not null && Items.Count > 0 ? LabelGap : 0)
                                + (Rows * Line) + (Math.Max(0, Rows - 1) * ItemGap);

        public Rect Bounds => new(Centre.X - (Width / 2), Centre.Y - (Height / 2), Width, Height);
    }

    private Words Measured(VennRegion region, IReadOnlyList<VennLayout.Circle> circles, IReadOnlyCollection<int> members)
    {
        var (centre, room) = VennLayout.Inside(circles, members)
                             ?? (new Point(members.Average(at => circles[at].Centre.X), members.Average(at => circles[at].Centre.Y)), 0);

        var ink = Colour(region.Style.Colour) ?? Colour(_diagram!.Config.SetTextColour) ?? Palette.Text;
        var size = region is VennSet ? SetSize : UnionSize;
        var letter = Text("x", size, ink);

        Written? label = region switch
        {
            { LabelHole: { } hole } => new Written(letter, null, hole, letter, ink),
            { Label: { Length: > 0 } written } => new Written(Text(Shown(written), size, ink, FontWeights.SemiBold), written, null, letter, ink),
            VennSet { NameHole: { } hole } => new Written(letter, null, hole, letter, ink),
            VennSet { Name: { Length: > 0 } name } => new Written(Text(Shown(name), size, ink, FontWeights.SemiBold), name, null, letter, ink),
            VennUnion union => new Written(Text(string.Join(" ∩ ", union.Sets), size, ink, FontWeights.SemiBold), null, null, letter, ink),
            _ => null,
        };

        var items = region.Items.Select(item =>
        {
            var itemInk = Colour(item.Style.Colour) ?? Palette.TextMuted;
            var itemLetter = Text("x", ItemSize, itemInk);

            return new Entry(item, item switch
            {
                { LabelHole: { } hole } => new Written(itemLetter, null, hole, itemLetter, itemInk),
                { Label: { Length: > 0 } written } => new Written(Text(Shown(written), ItemSize, itemInk), written, null, itemLetter, itemInk),
                { NameHole: { } hole } => new Written(itemLetter, null, hole, itemLetter, itemInk),
                _ => new Written(Text(Shown(item.Name), ItemSize, itemInk), item.Name, null, itemLetter, itemInk),
            });
        }).ToList();

        // A column of items reads down the region; only where a column would run out of it do they spread into more, as
        // Mermaid's grid does.
        var words = new Words(region, label, items, centre, room);
        while (words.Columns < items.Count && words.Height > room * 2) words = words with { Columns = words.Columns + 1 };

        return words;
    }

    private void Labels(LayoutBuilder build, IReadOnlyList<Words> regions, Vector shift)
    {
        build.Open(VennPiece.Labels, part: null, stops: Stops.None);

        foreach (var words in regions)
        {
            var bounds = words.Bounds;
            var top = bounds.Y + shift.Y;
            var middle = words.Centre.X + shift.X;

            build.Open(VennPiece.Region, words.Region.Part, stops: Stops.None);

            if (words.Label is { } label)
            {
                Set(build, label, new Point(middle - (label.Width / 2), top), VennPiece.Label);
                top += label.Height + (words.Items.Count > 0 ? LabelGap : 0);
            }

            var across = (words.Columns * words.Cell) + ((words.Columns - 1) * ItemApart);
            for (var at = 0; at < words.Items.Count; at++)
            {
                var (row, column) = Math.DivRem(at, words.Columns);
                var entry = words.Items[at];

                // Each item centred in its cell, so a column of different lengths still reads as a column.
                var cell = middle - (across / 2) + (column * (words.Cell + ItemApart));
                var place = new Point(cell + ((words.Cell - entry.Says.Width) / 2), top + (row * (words.Line + ItemGap)));

                build.Open(VennPiece.Item, entry.Item.Part, stops: Stops.None);
                Set(build, entry.Says, place, VennPiece.Text);
                build.Close();
            }

            build.Close();
        }

        build.Close();
    }

    /// <summary>
    /// Words where something was written — typed into where they are the characters written, and shown as written when pressed
    /// where they are not — a hole where nothing yet is, and words worked out where neither.
    /// </summary>
    private static void Set(LayoutBuilder build, Written written, Point at, string kind)
    {
        if (written.Hole is { } hole)
        {
            LayoutText.Hole(build, hole, at, written.Letter, written.Ink);
            return;
        }

        LayoutText.Words(build, written.Text, at, written.Text.Width, TextAlignment.Left, written.Part, kind,
                         maps: written.Maps, writes: written.Part is not null, ink: written.Ink);
    }

    // ── The workings ────────────────────────────────────────────────────────

    /// <summary>A cross at each circle's centre, and a box round where each region's words are set.</summary>
    private void Debug(LayoutBuilder build, IReadOnlyList<VennLayout.Circle> circles, IReadOnlyList<Words> regions, Vector shift)
    {
        build.Open(VennPiece.Debug, part: null, stops: Stops.None);

        var marks = new GeometryGroup();
        foreach (var circle in circles)
        {
            var centre = circle.Centre + shift;
            marks.Children.Add(new LineGeometry(centre + new Vector(-4, 0), centre + new Vector(4, 0)));
            marks.Children.Add(new LineGeometry(centre + new Vector(0, -4), centre + new Vector(0, 4)));
        }

        foreach (var words in regions)
        {
            var box = words.Bounds;
            box.Offset(shift);
            marks.Children.Add(new RectangleGeometry(box));
        }

        marks.Freeze();
        build.Draw(new GeometryMark(marks, null, Palette.Accent, 1));
        build.Close();
    }

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>A brush at <paramref name="opacity"/>.</summary>
    private static Brush Faded(Brush brush, double opacity)
    {
        if (opacity >= 1) return brush;

        var faded = brush.Clone();
        faded.Opacity = Math.Max(0, opacity);
        faded.Freeze();
        return faded;
    }
}
