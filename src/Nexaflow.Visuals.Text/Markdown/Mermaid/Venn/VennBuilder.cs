using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;

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

    internal VennBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>One item written inside a set or a union: a <c>text</c> line.</summary>
    /// <param name="Id">What it is called.</param>
    /// <param name="Part">The line it was written on, which is what a press on it means.</param>
    /// <param name="Name">Its name as written, without its quotes.</param>
    private sealed record Item(string Id, ContentPart Part, ContentPart Name)
    {
        public ContentPart? NameHole { get; init; }

        /// <summary>What its label says, without its brackets or quotes, or null where it has none.</summary>
        public ContentPart? Label { get; init; }

        public ContentPart? LabelHole { get; init; }

        public MermaidStyle Style { get; init; } = MermaidStyle.None;
    }

    /// <summary>A region: a set, or the overlap a union names.</summary>
    /// <param name="key">What the stages say it is known by.</param>
    /// <param name="part">The region that first wrote it — its line, and the items indented under it — which is what a press on it means.</param>
    private abstract class Region(string key, ContentPart part, double weight)
    {
        public string Key { get; } = key;

        public ContentPart Part { get; } = part;

        /// <summary>What its label says, or null where none is written. The last one written wins.</summary>
        public ContentPart? Label { get; set; }

        public ContentPart? LabelHole { get; set; }

        /// <summary>How much of the drawing it takes: the size written, or Mermaid's where none is or it is no size.</summary>
        public double Weight { get; set; } = weight;

        public MermaidStyle Style { get; } = StyleOf(part);

        /// <summary>The items written in it, in order.</summary>
        public List<Item> Items { get; } = [];
    }

    /// <summary>One set: a circle.</summary>
    /// <param name="Order">Where it comes among the sets, which is which colour of the palette it takes.</param>
    private sealed class Set(string id, ContentPart part, ContentPart name, int order) : Region(id, part, Unsized)
    {
        /// <summary>What a set is worth where no size is written — Mermaid's.</summary>
        public const double Unsized = 10;

        public string Id => Key;

        public ContentPart Name { get; } = name;

        public ContentPart? NameHole { get; init; }

        public int Order { get; } = order;

        /// <summary>The colour the front matter writes for its place in the order, or null to leave it to the theme.</summary>
        public string? Colour { get; init; }
    }

    /// <summary>A union: where two sets or more overlap.</summary>
    /// <param name="sets">The names of the sets it is the overlap of, sorted.</param>
    private sealed class Union(string key, ContentPart part, IReadOnlyList<string> sets)
        : Region(key, part, Set.Unsized / Math.Max(1, sets.Count * sets.Count))
    {
        public IReadOnlyList<string> Sets { get; } = sets;
    }

    /// <summary>
    /// The sets in the order they were first written, the unions where they overlap and the items written in each, read down
    /// the tree. A set written twice is one set, its later label and size winning; a union naming a set not written above it,
    /// or fewer than two, is no overlap and is not drawn — the reason already on its line.
    /// </summary>
    private sealed class Diagram
    {
        private Diagram(VennConfig config) => Config = config;

        public VennConfig Config { get; }

        public List<Set> Sets { get; } = [];

        public List<Union> Unions { get; } = [];

        public static Diagram Of(ContentPart root, VennConfig config)
        {
            var diagram = new Diagram(config);
            var loose = new List<ContentPart>();

            foreach (var part in root.Children)
            {
                if (part.Kind == VennKinds.Region)
                {
                    var region = part.Children[0].Stated() switch
                    {
                        { Kind: VennKinds.Set } set => diagram.Declared(part, set),
                        { Kind: VennKinds.Union } union => diagram.Overlapped(part, union),
                        _ => null,
                    };

                    foreach (var line in part.Children.Skip(1))
                        if (region is not null && line.Stated() is { Kind: VennKinds.Text } item && Itemed(item) is { } read)
                            region.Items.Add(read);

                    continue;
                }

                // An item on its own may name a region written after it, so it is put there once every region is known.
                if (part.Stated() is { Kind: VennKinds.Text } written) loose.Add(written);
            }

            foreach (var item in loose)
            {
                if (item.Fact(VennRoles.Key) is not { } key || Itemed(item) is not { } read) continue;

                Region? region = diagram.Sets.FirstOrDefault(set => set.Key == key);
                region ??= diagram.Unions.FirstOrDefault(union => union.Key == key);
                region?.Items.Add(read);
            }

            return diagram;
        }

        /// <summary>
        /// How much two sets overlap: the size of the union written for the pair of them — or, where they are only two of the
        /// sets a larger union overlaps, a quarter of the smaller, which is what gives that union a region to sit in, as
        /// Mermaid does — and nought where nothing says they overlap at all.
        /// </summary>
        public double Overlap(Set one, Set other)
        {
            if (Unions.FirstOrDefault(union => union.Sets.Count == 2 && union.Sets.Contains(one.Id) && union.Sets.Contains(other.Id)) is { } written)
                return written.Weight;

            return Unions.Any(union => union.Sets.Contains(one.Id) && union.Sets.Contains(other.Id))
                ? Math.Min(one.Weight, other.Weight) / 4
                : 0;
        }

        private Region? Declared(ContentPart region, ContentPart line)
        {
            if (Id(line) is not { } name) return null;

            var id = region.Fact(VennRoles.Key) ?? string.Empty;
            var set = Sets.FirstOrDefault(set => set.Id == id);
            if (set is null)
            {
                Sets.Add(set = new Set(id, region, name, Sets.Count)
                {
                    NameHole = name.Parent.Hole(),
                    Colour = Config.Swatches.GetValueOrDefault((Sets.Count % VennConfig.PaletteSize) + 1),
                });
            }

            Written(set, line);
            return set;
        }

        private Region? Overlapped(ContentPart region, ContentPart line)
        {
            var key = region.Fact(VennRoles.Key) ?? string.Empty;
            var names = key.Split(',', StringSplitOptions.RemoveEmptyEntries);

            // Only sets written above it: that is what a union is the overlap of.
            if (names.Length < 2 || names.Any(name => Sets.All(set => set.Id != name))) return null;

            var union = Unions.FirstOrDefault(union => union.Key == key);
            if (union is null) Unions.Add(union = new Union(key, region, names));

            Written(union, line);
            return union;
        }

        /// <summary>A set's or a union's label and size, where the line writes them: a later line's win.</summary>
        private static void Written(Region region, ContentPart line)
        {
            if (line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label) is { } label)
            {
                region.Label = label.Words();
                region.LabelHole = label.Hole();
            }

            if (line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Amount).Inner(MermaidKinds.Number)?.Number() is { } size)
                region.Weight = size;
        }

        /// <summary>An item, from its <c>text</c> line — or null for one with no name to go by.</summary>
        private static Item? Itemed(ContentPart line)
        {
            if (Id(line) is not { } name) return null;

            var label = line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
            return new Item(name.Text, line, name)
            {
                NameHole = name.Parent.Hole(),
                Label = label.Words(),
                LabelHole = label.Hole(),
                Style = StyleOf(line),
            };
        }

        /// <summary>The name a line writes directly on it — a set's, or an item's past the region it names — without its quotes.</summary>
        private static ContentPart? Id(ContentPart line) =>
            line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words();
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>The front matter's <c>vennTitleTextColor</c>, where it writes one.</summary>
    protected override string? TitleColour => Configured(VennConfig.Default).TitleTextColour;

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = Diagram.Of(Reading.Root, Configured(VennConfig.Default));
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

    private static int Order(IReadOnlyList<Set> sets, string id)
    {
        for (var at = 0; at < sets.Count; at++)
            if (sets[at].Id == id) return at;

        return -1;
    }

    // ── The circles ─────────────────────────────────────────────────────────

    private void Circles(LayoutBuilder build, IReadOnlyList<Set> sets, IReadOnlyList<VennLayout.Circle> circles,
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

            var fill = Fill(set);

            build.Open(VennPiece.Circle, set.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, DiagramInk.Faded(fill, set.Style.FillOpacity ?? FillOpacity),
                                        Ink.Written(set.Style.Stroke) ?? fill, set.Style.StrokeWidth ?? StrokeWidth));
            build.Occupies(stands);
            build.Close();
        }

        build.Close();
    }

    /// <summary>What a set is drawn in: its style's fill, the front matter's colour for its place, or the theme's next series colour.</summary>
    private Brush Fill(Set set) => Ink.Written(set.Style.Fill) ?? Ink.Series(set.Order, set.Colour);

    // ── The overlaps ────────────────────────────────────────────────────────

    /// <summary>A union's overlap: the lens its circles make, and the part of it no other circle covers.</summary>
    private sealed record Lens(Union Union, IReadOnlySet<int> Members, Geometry Whole, Geometry Own);

    /// <summary>Where a union's circles meet — or null where they do not.</summary>
    private static Lens? Lensed(Union union, IReadOnlyList<Set> sets, IReadOnlyList<VennLayout.Circle> circles, Vector shift)
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

            var fill = Ink.Written(union.Style.Fill);
            var stroke = Ink.Written(union.Style.Stroke);
            if (fill is not null || stroke is not null)
                build.Draw(new GeometryMark(whole, fill is null ? null : DiagramInk.Faded(fill, union.Style.FillOpacity ?? FillOpacity), stroke,
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

    /// <summary>One item, measured.</summary>
    private sealed record Entry(Item Item, DiagramWords Says);

    /// <summary>A region's words, measured and placed: its label on top, and its items in a grid under it.</summary>
    /// <param name="Room">How far the point they are centred on is from the region's nearest edge, or nought where the region has no room.</param>
    /// <param name="Columns">How many columns the items are set in.</param>
    private sealed record Words(Region Region, DiagramWords? Label, IReadOnlyList<Entry> Items, Point Centre, double Room, int Columns = 1)
    {
        public int Rows => Items.Count == 0 ? 0 : (int)Math.Ceiling(Items.Count / (double)Columns);

        public double Cell => Items.Count == 0 ? 0 : Items.Max(entry => entry.Says.Width);

        public double Line => Items.Count == 0 ? 0 : Items.Max(entry => entry.Says.Height);

        public double Width => Math.Max(Label?.Width ?? 0, Items.Count == 0 ? 0 : (Columns * Cell) + ((Columns - 1) * ItemApart));

        public double Height => (Label?.Height ?? 0) + (Label is not null && Items.Count > 0 ? LabelGap : 0)
                                + (Rows * Line) + (Math.Max(0, Rows - 1) * ItemGap);

        public Rect Bounds => new(Centre.X - (Width / 2), Centre.Y - (Height / 2), Width, Height);
    }

    private Words Measured(Region region, IReadOnlyList<VennLayout.Circle> circles, IReadOnlyCollection<int> members)
    {
        var (centre, room) = VennLayout.Inside(circles, members)
                             ?? (new Point(members.Average(at => circles[at].Centre.X), members.Average(at => circles[at].Centre.Y)), 0);

        var ink = Ink.Written(region.Style.Colour) ?? Ink.Written(Configured(VennConfig.Default).SetTextColour) ?? Palette.Text;
        var size = region is Set ? SetSize : UnionSize;

        // Its label; a set with none, its name; a union with none, the names of the sets it overlaps, which nobody wrote there.
        var label = region switch
        {
            { LabelHole: not null } or { Label.Length: > 0 } => Written(region.Label, region.LabelHole, size, ink, FontWeights.SemiBold),
            Set set when set.NameHole is not null || set.Name.Length > 0 => Written(set.Name, set.NameHole, size, ink, FontWeights.SemiBold),
            Union union => Worked(string.Join(" ∩ ", union.Sets), null, size, ink, FontWeights.SemiBold),
            _ => null,
        };

        var items = region.Items.Select(item =>
        {
            var said = Ink.Written(item.Style.Colour) ?? Palette.TextMuted;
            return new Entry(item, item.LabelHole is not null || item.Label is { Length: > 0 }
                                       ? Written(item.Label, item.LabelHole, ItemSize, said)
                                       : Written(item.Name, item.NameHole, ItemSize, said));
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
                label.Set(build, new Point(middle - (label.Width / 2), top), VennPiece.Label);
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
                entry.Says.Set(build, place, VennPiece.Text);
                build.Close();
            }

            build.Close();
        }

        build.Close();
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
}
