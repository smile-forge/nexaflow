using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Class;

/// <summary>
/// Draws a <c>classDiagram</c> — or a <c>classDiagram-v2</c>, which Mermaid reads the same way. The classes are laid out in ranks
/// by how far along the relations reach them (<see cref="PortableDiagramLayers"/>), each rank ordered so as few lines cross as can be
/// managed, and the whole thing runs the way a <c>direction</c> line asks.
///
/// <strong>A class is three compartments in one box.</strong> Its name — with what an annotation says it is over the top of it —
/// then its fields, then its methods, each band divided by a rule the width of the box. Which band a member goes in is Mermaid's
/// rule: brackets after its name make it a method.
///
/// <strong>A namespace holds its classes in the layout.</strong> What is inside one is laid out in its own space and drawn inside
/// the namespace's piece, so pressing a class means that class and pressing the room round it means the namespace.
/// </summary>
internal sealed partial class PortableClassDrawing
{
    /// <summary>How big a class's name is drawn, its members, and what an annotation says it is.</summary>
    private const double TextSize = 12;
    private const double MemberSize = 12;
    private const double KindSize = 10.5;

    /// <summary>How big what is written on a relation is, and how many of one class the other has.</summary>
    private const double LabelSize = 11;

    /// <summary>How deep one row of a class is — its name, or one member — which is a rhythm rather than the words' own height.</summary>
    private const double Row = 18;

    /// <summary>The clear air either side of a class's rows, and above and below each band of them.</summary>
    private const double Pad = 12;
    private const double Air = 5;

    /// <summary>The clear air inside a namespace's box.</summary>
    private const double Boxed = 14;

    /// <summary>The air a count leaves between itself and the end of the line it belongs to.</summary>
    private const double Tight = 1;

    /// <summary>And how far off the line it stands, so the line is not drawn through it.</summary>
    private const double Aside = 4;

    /// <summary>The least room a class takes, so classes with little in them come out alike.</summary>
    private const double Least = 96;
    private const double Shortest = 34;

    /// <summary>The room a border takes round a class, which is drawn inside the box rather than over its edge.</summary>
    private const double Chrome = 4;

    /// <summary>The stub a lollipop hangs on, the circle at the end of it, the band one takes, and how far apart they are set.</summary>
    private const double Stub = 20;
    private const double Ring = 5.5;
    private const double Band = 52;
    private const double Apart = 50;

    /// <summary>How thick a class is drawn, and the rules dividing it.</summary>
    private const double Thick = 1.5;

    /// <summary>How wide what is written on a relation runs before it wraps.</summary>
    private const double Widest = 160;

    /// <summary>What an annotation is drawn between, which is what Mermaid draws.</summary>
    private const string Opens = "«";
    private const string Shuts = "»";

    /// <param name="ink">What the builder draws in.</param>
    /// <param name="words">What the builder makes its words with.</param>
    /// <param name="sheet">The sheet the builder draws on.</param>
    /// <param name="folded">How much of the diagram the builder draws.</param>
    /// <param name="reader">How the block's lines are read into what is drawn, where they are not a class diagram's own — a nomnoml block's.</param>
    internal PortableClassDrawing(ContentReading reading, EditState state, PortableStyle style, ILayoutMedium medium, PortableDiagramInk ink,
                                  PortableDiagramText words, PortableDiagramSheet sheet, PortableDiagramFolding folded,
                                Func<ContentPart, ClassConfig, Diagram>? reader = null)
    {
        Reading = reading;
        State = state;
        Style = style;
        Medium = medium;
        Ink = ink;
        Words = words;
        Sheet = sheet;
        Folded = folded;
        Reader = reader;
    }

    private ContentReading Reading { get; }

    private EditState State { get; }

    private PortableStyle Style { get; }

    private ILayoutMedium Medium { get; }

    private PortableDiagramInk Ink { get; }

    private PortableDiagramText Words { get; }

    private PortableDiagramSheet Sheet { get; }

    private PortableDiagramFolding Folded { get; }

    /// <summary>How the block's lines are read, where they are not a class diagram's own.</summary>
    private Func<ContentPart, ClassConfig, Diagram>? Reader { get; }


    public LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        var diagram = Read(Reading.Root, PortableDiagramConfig.Of(Reading, ClassConfig.Default));

        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Nodes.Count == 0 && diagram.Spaces.Count == 0) return Sheet.AsWritten();

        // How much of it is drawn, worked out before anything is placed.
        Folded.Fold(new DiagramChart([.. diagram.Nodes.Select(node => node.Id)], [.. diagram.Relations.Select(relation => (relation.From, relation.To))]));

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The relations are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var tethers = Tethers(plan, room);
        var over = PortableDiagramConnector.Covered(build.Medium, [.. routes.Select(route => (route.Along, route.Room)),
                                                                   .. tethers.Select(tether => (tether.Along, LayoutRect.Empty))], Thick);

        build.Open(ClassPiece.Classes, part: null, stops: PortableStops.None);
        foreach (var space in diagram.Within(null)) Held(build, diagram, plan, room, space, over);
        foreach (var node in diagram.Inside(null)) Drawn(build, plan, room, node, over);
        foreach (var note in plan.Notes.Where(note => note.Space is null)) Noted(build, room, note, over);
        plan.Spill.Draw(build, room, Ink.Surface, new PortableDiagramStroke(Style.CodeBorder, 1, PortableDiagramStroke.Dotted));
        build.Close();

        // The band each namespace keeps at the top of itself for its own name, so nothing written against a line landing
        // inside one is set on top of the name already written there.
        var named = plan.Spaces.Values
            .Where(space => space.Cell.Heading > 0)
            .Select(space => room.At(space.Cell.Bounds) is var bounds
                ? new LayoutRect(bounds.X, bounds.Y, bounds.Width, space.Cell.Heading)
                : default)
            .ToList();

        Relations(build, routes, tethers, named);

        return room.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    /// <summary>
    /// Everything measured and placed: a cell for each class, each namespace and each note, a join for each relation, and the
    /// layered layout run over the lot of them.
    /// </summary>
    private Plan Laid(Diagram diagram)
    {
        var plan = new Plan();
        var cells = new List<PortableDiagramCell>();
        var towards = diagram.Way;

        foreach (var space in Nested(diagram, null))
        {
            var words = Naming(space, diagram.Config);
            var said = PortableDiagramWords.Taken(words);

            var box = new Box(space, words)
            {
                Cell = new PortableDiagramCell(new LayoutSize(said.Width + (Boxed * 2), 0))
                {
                    Inside = space.Parent is { } parent && plan.Spaces.TryGetValue(parent, out var held) ? held.Cell : null,
                    Pad = Boxed,
                    Heading = said.Height > 0 ? said.Height + diagram.Config.TitleMargin + (Boxed / 2) : 0,
                },
            };

            plan.Spaces[space.Key] = box;
            cells.Add(box.Cell);
        }

        foreach (var node in diagram.Nodes)
        {
            // A class folded away is never given a cell, so the relations to it have no end to meet and a namespace
            // holding nothing else closes up rather than standing empty.
            if (!Folded.Draws(node.Id)) continue;

            var sized = Measure(node, diagram.Config);
            sized.Cell = new PortableDiagramCell(sized.Size)
            {
                Inside = node.Group is { } group && plan.Spaces.TryGetValue(group, out var box) ? box.Cell : null,
            };

            plan.Nodes.Add(sized);
            plan.Named.TryAdd(node.Id, sized);
            cells.Add(sized.Cell);
        }

        foreach (var relation in diagram.Relations)
        {
            if (!plan.Named.TryGetValue(relation.From, out var from) || !plan.Named.TryGetValue(relation.To, out var to)) continue;

            // What is written on the line goes with it, so the layout keeps room for it where it takes the line round
            // something and when it holds the ranks apart.
            plan.Joins[relation] = new PortableDiagramJoin(from.Cell, to.Cell)
            {
                Said = Says(relation) is { Count: > 0 } words ? PortableDiagramWords.Taken(words) : default,
            };
        }

        // A note is written about the class it is beside, so it is held in the same rank rather than after it.
        foreach (var note in diagram.Notes)
        {
            var words = Says(note, diagram.Config);
            var taken = PortableDiagramWords.Taken(words);
            var beside = new Pinned(note, words, diagram.Find(note.Of)?.Group)
            {
                Cell = new PortableDiagramCell(new LayoutSize(taken.Width + (Pad * 2), taken.Height + (Pad * 2)))
                {
                    Inside = plan.Named.TryGetValue(note.Of, out var of) ? of.Cell.Inside : null,
                },
            };

            plan.Notes.Add(beside);
            cells.Add(beside.Cell);

            if (plan.Named.TryGetValue(note.Of, out var about))
            {
                beside.Join = new PortableDiagramJoin(about.Cell, beside.Cell, span: 0);
                plan.Beside.Add(beside.Join);
            }
        }

        plan.Spill = Folded.Spilled(id => plan.Named.TryGetValue(id, out var sized) ? sized.Cell : null);
        cells.AddRange(plan.Spill.Cells);

        plan.Size = PortableDiagramLayers.Lay(cells, [.. plan.Joins.Values, .. plan.Beside, .. plan.Spill.Joins], towards,
                                              diagram.Config.NodeSpacing, diagram.Config.RankSpacing, square: true,
                                              room: Sheet.Space);

        return plan;
    }

    /// <summary>The namespaces, each before the ones nested in it, so a nested one is measured after the box it sits in.</summary>
    private static IEnumerable<Namespace> Nested(Diagram diagram, string? inside)
    {
        foreach (var space in diagram.Within(inside))
        {
            yield return space;
            foreach (var held in Nested(diagram, space.Key)) yield return held;
        }
    }

    /// <summary>Everything the diagram means to draw, gathered so the whole of it is brought inside the box the block takes.</summary>
    private PortableDiagramRoom Reached(Diagram diagram, Plan plan) =>
        PortableDiagramRoom.Round(diagram.Config.Padding, plan.Size,
                                  [.. plan.Nodes.Select(node => node.Cell), .. plan.Notes.Select(note => note.Cell),
                           .. plan.Spaces.Values.Select(box => box.Cell)],
                                  plan.Joins.Select(join => (join.Value, Says(join.Key))));

    // ── What a class comes to ───────────────────────────────────────────────

    /// <summary>
    /// A class measured: the words of each band, how deep each band is, and how much room the whole of it takes — the box, and
    /// the bands above and below it that its lollipops hang in.
    /// </summary>
    /// <remarks>
    /// The rows keep Mermaid's own rhythm rather than the height the words turned out to be, so boxes line up with each other
    /// whatever is written in them. What is measured rather than guessed is the width: a box is as wide as its longest row.
    /// </remarks>
    private Sized Measure(Node node, ClassConfig config)
    {
        var ink = Ink.Written(node.Style.Colour) ?? Style.Text;

        var title = new List<PortableDiagramWords>();
        if (node.Kind is { Length: > 0 } kind)
            title.Add(Words.Worked(Opens + kind.Text + Shuts, kind, KindSize, ink, italic: true));

        title.AddRange(Called(node, ink, config));

        var fields = node.Fields.Select(member => (Member: member, Words: Lettered(member, ink))).ToList();
        var methods = node.Methods.Select(member => (Member: member, Words: Lettered(member, ink))).ToList();
        var offered = node.Lollipops.Select(lollipop => (Lollipop: lollipop, Words: Offered(lollipop))).ToList();

        var bands = new List<PortableDiagramCompartment>
        {
            new([.. title.Select(words => PortableDiagramRow.Of(words))]) { Centred = true },
        };

        // The two bands under the name are drawn whether or not anything is written in them, unless the front matter says not.
        if (!config.HideEmptyMembers || fields.Count > 0 || methods.Count > 0)
        {
            bands.Add(new([.. fields.Select(row => PortableDiagramRow.Of(row.Words))]));
            bands.Add(new([.. methods.Select(row => PortableDiagramRow.Of(row.Words))]));
        }

        var laid = PortableDiagramBox.Measure(bands, Row, Pad, Air, gap: 0, new LayoutSize(Least, Shortest), Chrome);

        var above = offered.Any(one => !one.Lollipop.Below) ? Band : 0;
        var below = offered.Any(one => one.Lollipop.Below) ? Band : 0;

        var spread = offered.Count == 0
            ? 0
            : Math.Max(offered.Count(one => !one.Lollipop.Below), offered.Count(one => one.Lollipop.Below)) * Apart;

        return new Sized(node, fields, methods, offered, laid)
        {
            Above = above,
            Below = below,
            Size = new LayoutSize(Math.Max(laid.Size.Width, spread), laid.Size.Height + above + below),
        };
    }

    /// <summary>What a class is called: its label or its id, with its type parameters after it in angle brackets.</summary>
    private IReadOnlyList<PortableDiagramWords> Called(Node node, LayoutInk ink, ClassConfig config)
    {
        if (node.Generic is not { Length: > 0 } generic)
            return Words.Wrapped(node.Said, node.SaidHole, TextSize, ink, config.Wrapping, 600);

        return [Words.Worked($"{node.Said?.Text ?? node.Id}<{generic}>", node.Part, TextSize, ink, 600)];
    }

    /// <summary>What a lollipop's circle is labelled with, which nobody writes twice and so is pressed as where it was written.</summary>
    private PortableDiagramWords Offered(Lollipop lollipop) => Words.Written(lollipop.Part, null, LabelSize, Style.Text);

    /// <summary>
    /// One member as it is drawn: the characters written, where that is what it says — a member whose type parameters are
    /// angled, whose classifier is taken off, which gives something back or which points somewhere is worked out, and so
    /// pressed rather than typed into. A member that leads somewhere is drawn in the colour a link is drawn in.
    /// </summary>
    private PortableDiagramWords Lettered(Member member, LayoutInk ink)
    {
        var paint = member.Href is { Length: > 0 } ? Style.Accent : ink;
        var slant = member.Abstract ? true : (bool?)null;

        return member.Hole is not null || member.Written
            ? Words.Written(member.Part, member.Hole, MemberSize, paint, italic: slant)
            : Words.Worked(member.Says, member.Part, MemberSize, paint, italic: slant);
    }

    /// <summary>What is written on a relation, where anything is.</summary>
    private IReadOnlyList<PortableDiagramWords> Says(Relation relation) =>
        relation.Said is null && relation.SaidHole is null
            ? []
            : Words.Wrapped(relation.Said, relation.SaidHole, LabelSize, Style.Text, Widest);

    /// <summary>What a note says.</summary>
    private IReadOnlyList<PortableDiagramWords> Says(Note note, ClassConfig config) =>
        Words.Wrapped(note.Said, note.SaidHole, LabelSize, Style.Text, config.Wrapping);

    /// <summary>What is written at the top of a namespace.</summary>
    private IReadOnlyList<PortableDiagramWords> Naming(Namespace space, ClassConfig config) =>
        space.Said is not null
            ? Words.Wrapped(space.Said, null, TextSize, Style.Text, config.Wrapping)
            : [Words.Worked(space.Name, space.Part, TextSize, Style.Text)];

    // ── The relations ───────────────────────────────────────────────────────

    /// <summary>Where every relation runs once everything is placed, its ends brought in to the boxes it joins.</summary>
    private List<Route> Routes(Diagram diagram, Plan plan, PortableDiagramRoom room)
    {
        var routes = new List<Route>();

        foreach (var relation in diagram.Relations)
        {
            if (!plan.Joins.TryGetValue(relation, out var join) || join.Route.Count < 2) continue;

            var along = PortableDiagramConnector.Trimmed(join);
            var placed = along.Select(room.At).ToList();
            var said = Says(relation);

            // On the longest run of the line rather than its own middle: a line that had to turn to get past something has
            // its middle at the turn, which is the one place along it that is squeezing past what it turned for.
            routes.Add(new Route(relation, placed, said, PortableDiagramConnector.Room(placed, said, PortableDiagramConnector.Longest(placed)))
            {
                Near = Counted(relation.Near),
                Far = Counted(relation.Far),
            });
        }

        return routes;
    }

    /// <summary>Where the line holding each note to its class runs: from the edge of the one to the edge of the other.</summary>
    private static List<(Pinned Note, IReadOnlyList<LayoutPoint> Along)> Tethers(Plan plan, PortableDiagramRoom room) =>
    [
        .. plan.Notes
            .Where(note => note.Join is { Route.Count: >= 2 })
            .Select(note => (note, (IReadOnlyList<LayoutPoint>)[.. PortableDiagramConnector.Trimmed(note.Join!).Select(room.At)])),
    ];

    private PortableDiagramWords? Counted(ContentPart? count) =>
        count is { Length: > 0 } ? Words.Written(count, null, LabelSize, Style.TextMuted) : null;

    /// <summary>
    /// Every relation: the lines first, and then everything written on them.
    ///
    /// <para>
    /// In that order because a line is drawn over whatever is already there, and the lines of a diagram cross one another.
    /// Drawing each line with its own words before the next line is drawn puts the next line through those words — which no
    /// backing behind them can help, since the backing went down before the line did.
    /// </para>
    /// </summary>
    private void Relations(PortableBuilder build, IReadOnlyList<Route> routes, IReadOnlyList<(Pinned Note, IReadOnlyList<LayoutPoint> Along)> tethers,
                           IReadOnlyList<LayoutRect> named)
    {
        if (routes.Count == 0 && tethers.Count == 0) return;

        build.Open(ClassPiece.Relations, part: null, stops: PortableStops.None);

        // A note is held to its class by a dashed line in the note's own colour, so it is plain which class it is about and
        // that the line is not a relation.
        foreach (var (note, along) in tethers)
            PortableDiagramConnector.Draw(build, ClassPiece.Tether, note.Note.Part, along, new PortableDiagramStroke(Ink.NoteEdge, 1, PortableDiagramStroke.Dashed),
                                          DiagramHead.None, DiagramHead.None);

        foreach (var route in routes)
        {
            var stroke = new PortableDiagramStroke(Ink.Link, Thick, route.Relation.Dotted ? PortableDiagramStroke.Dashed : null);

            // Square, corners and all: several relations reaching the same class run up to the same rail and into it by
            // the same stem, and a rounded corner is a corner that no longer meets the next one.
            PortableDiagramConnector.Draw(build, ClassPiece.Relation, route.Relation.Part, route.Along, stroke,
                                          route.Relation.Head, route.Relation.Tail);
        }

        foreach (var route in routes)
        {
            // On the page's own surface rather than the code background, which is see-through on the light theme: what is
            // written on a line has the line running under it, and a backing that lets the line through is no backing.
            PortableDiagramConnector.Says(build, ClassPiece.Label, route.Relation.Part, route.Room, route.Said, Ink.Surface);

            // Each count is set against the run of line leaving its own end, which a bend further along does not turn.
            Counting(build, route.Near, route.Along[0], route.Along[1], named);
            Counting(build, route.Far, route.Along[^1], route.Along[^2], named);
        }

        build.Close();
    }

    /// <summary>
    /// What is counted at one end of a relation, set tight against the point the line leaves or arrives at.
    ///
    /// <para>
    /// Tight, because that is the whole of what says which end it counts: two counts on one line are told apart by which
    /// end each is near, so a count held out to clear something else is a count that could belong to either of them. The
    /// one thing it is moved for is a band a namespace keeps for its own name, which already has the name written on it.
    /// </para>
    /// </summary>
    private static void Counting(PortableBuilder build, PortableDiagramWords? count, LayoutPoint end, LayoutPoint toward,
                                 IReadOnlyList<LayoutRect> named)
    {
        if (count is null) return;

        var run = toward - end;
        var most = run.Length;
        if (most > 0) run /= most;

        var aside = new LayoutVector(-run.Y, run.X) * ((count.Height / 2) + Aside);
        var back = (count.Height / 2) + Tight;

        // Off the band a little way, rather than flush against it: a count touching the box it was moved out of still reads
        // as being on it. Edged along rather than stepped over it, and stopping a hair short of the turn the line takes, so
        // a count that cannot get clear stops just off the turn rather than halfway across the border it was leaving — and
        // with the same air from the line it turns into that it keeps from the end it belongs to.
        var stop = Math.Max(back, most - Tight);

        while (back < stop
               && named.Any(band => LayoutRect.Inflate(band, Aside / 2, Aside / 2).IntersectsWith(Counted(end + (run * back) + aside, count))))
            back = Math.Min(stop, back + 1);

        var at = end + (run * back) + aside;

        count.Set(build, new LayoutPoint(at.X - (count.Width / 2), at.Y - (count.Height / 2)), ClassPiece.Count);
    }

    /// <summary>The box a count takes, set about a point.</summary>
    private static LayoutRect Counted(LayoutPoint at, PortableDiagramWords count) =>
        new(at.X - (count.Width / 2), at.Y - (count.Height / 2), count.Width, count.Height);

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>A namespace: its box, its name at the top of it, and the classes it holds drawn inside its piece.</summary>
    private void Held(PortableBuilder build, Diagram diagram, Plan plan, PortableDiagramRoom room, Namespace space,
                      IReadOnlyList<LayoutShape> over)
    {
        var box = plan.Spaces[space.Key];
        var bounds = room.At(box.Cell.Bounds);
        var said = PortableDiagramWords.Taken(box.Words);
        var heading = new LayoutRect(bounds.X + Boxed, bounds.Y + (Boxed / 3), Math.Max(0, bounds.Width - (Boxed * 2)), said.Height);

        var covered = PortableDiagramShapes.United(build.Medium, 
        [
            .. over,
            .. diagram.Within(space.Key).Select(nested => PortableDiagramShapes.Outline(build.Medium, DiagramShape.Rounded, room.At(plan.Spaces[nested.Key].Cell.Bounds))),
            .. Inside(diagram, plan, space.Key).Select(node => PortableDiagramShapes.Outline(build.Medium, DiagramShape.Rectangle, room.At(node.Cell.Bounds))),
            .. Noting(plan, space.Key).Select(note => PortableDiagramShapes.Outline(build.Medium, DiagramShape.Card, room.At(note.Cell.Bounds))),
        ]);

        build.Open(ClassPiece.Space, space.Whole, stops: PortableStops.None);
        PortableDiagramShapes.Draw(build, ClassPiece.Holding, space.Part, DiagramShape.Rounded, bounds, Ink.Group,
                                   new PortableDiagramStroke(Ink.GroupEdge, Thick), PortableDiagramWords.Placed(box.Words, heading, MermaidPiece.Words), covered,
                                   band: Ink.Band(null));

        foreach (var nested in diagram.Within(space.Key)) Held(build, diagram, plan, room, nested, over);
        foreach (var node in diagram.Inside(space.Key)) Drawn(build, plan, room, node, over);
        foreach (var note in Noting(plan, space.Key)) Noted(build, room, note, over);
        build.Close();
    }

    /// <summary>
    /// One class: its box, the rules dividing it, its name, its members in the band each belongs to, and the interfaces it
    /// offers on stubs off the top and the bottom of it.
    /// </summary>
    private void Drawn(PortableBuilder build, Plan plan, PortableDiagramRoom room, Node node, IReadOnlyList<LayoutShape> over)
    {
        if (plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)) is not { } sized) return;

        var bounds = room.At(sized.Cell.Bounds);

        // A class carrying lollipops is given room for them either side; the box itself sits in the middle of that.
        var box = new LayoutRect(bounds.X + ((bounds.Width - sized.Wide) / 2), bounds.Y + sized.Above, sized.Wide, sized.Deep);
        var outline = PortableDiagramShapes.Outline(build.Medium, DiagramShape.Rounded, box);
        var placed = sized.Laid.Placed(box).ToList();

        var covered = new List<LayoutShape>();
        foreach (var shape in over) covered.Add(shape);
        foreach (var (_, _, set) in placed)
            foreach (var (words, at) in set) covered.Add(build.Medium.Rectangle(new LayoutRect(at, new LayoutSize(words.Width, words.Height))));

        build.Open(ClassPiece.Class, node.Whole, stops: PortableStops.None);
        if (node.Href is { Length: > 0 } href) build.Links(href, node.Tip);

        build.Open(MermaidPiece.Shape, node.Part, stops: PortableStops.None);
        var stroke = Stroke(node.Style);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, outline, Fill(node), stroke.Ink, stroke.Thickness));

        // The rules divide the box into its bands, which is what says a class has members at all — in its outline's ink, fainter.
        var rule = PortableDiagramInk.Ruled(build.Medium, stroke.Ink);
        foreach (var at in sized.Laid.Rules(box))
            build.Draw(new PortableLineMark(new LayoutPoint(box.Left, at), new LayoutPoint(box.Right, at), rule));

        var stands = build.Medium.Combine(outline, build.Medium.Group(covered), ShapeCombine.Exclude);
        build.Occupies(stands);
        build.Close();

        foreach (var (band, at, set) in placed)
            foreach (var (words, where) in set)
            {
                if (band == 0) words.Set(build, where, MermaidPiece.Words);
                else Membered(build, words, where, band == 1 ? sized.Fielded[at].Member : sized.Methoded[at].Member);
            }

        Offering(build, sized, box, above: true);
        Offering(build, sized, box, above: false);

        build.Close();

        Folded.Chipped(build, node.Id, box, node.Whole);
    }

    /// <summary>
    /// The interfaces a class offers on one side of it: a stub off the edge, a small circle at the end of it, and the name
    /// just past the circle — which is how Mermaid draws a lollipop.
    /// </summary>
    private void Offering(PortableBuilder build, Sized sized, LayoutRect box, bool above)
    {
        var offered = sized.Offered.Where(one => one.Lollipop.Below != above).ToList();
        if (offered.Count == 0) return;

        var edge = above ? box.Top : box.Bottom;
        var way = above ? -1 : 1;
        var middle = box.X + (box.Width / 2);

        for (var at = 0; at < offered.Count; at++)
        {
            var (lollipop, words) = offered[at];
            var across = middle + ((at - ((offered.Count - 1) / 2.0)) * Apart);
            var end = edge + (way * Stub);
            var centre = new LayoutPoint(across, end + (way * Ring));

            build.Open(ClassPiece.Lollipop, lollipop.Part, stops: PortableStops.None);
            build.Draw(new PortableLineMark(new LayoutPoint(across, edge), new LayoutPoint(across, end), Style.CodeBorder));
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Ellipse(centre, Ring, Ring), Style.CodeBg, Style.CodeBorder, Thick));

            words.Set(build, new LayoutPoint(across - (words.Width / 2),
                                             above ? centre.Y - Ring - 2 - words.Height : centre.Y + Ring + 2),
                      MermaidPiece.Words);
            build.Close();
        }
    }

    /// <summary>One member, set — as a piece of its own where it leads somewhere, so a press on it means the link.</summary>
    private static void Membered(PortableBuilder build, PortableDiagramWords words, LayoutPoint at, Member member)
    {
        if (member.Href is { Length: > 0 } href)
        {
            build.Open(ClassPiece.Member, member.Part, stops: PortableStops.None);
            build.Links(href);
            words.Set(build, at, MermaidPiece.Words);
            build.Close();
        }
        else
        {
            words.Set(build, at, MermaidPiece.Words);
        }

        // A member Mermaid draws underlined is one held by the class rather than by anything made of it.
        if (member.Fixed)
            build.Draw(new PortableLineMark(new LayoutPoint(at.X, at.Y + words.Height - 1), new LayoutPoint(at.X + words.Width, at.Y + words.Height - 1),
                                            words.Ink));
    }

    /// <summary>A note: what it says in a box of its own, beside the class it is about.</summary>
    private void Noted(PortableBuilder build, PortableDiagramRoom room, Pinned note, IReadOnlyList<LayoutShape> over)
    {
        var bounds = room.At(note.Cell.Bounds);
        var words = PortableDiagramWords.Placed(note.Words, PortableDiagramShapes.Inside(DiagramShape.Card, bounds), MermaidPiece.Words);

        PortableDiagramShapes.Draw(build, ClassPiece.Note, note.Note.Part, DiagramShape.Card, bounds, Ink.Note,
                                   new PortableDiagramStroke(Ink.NoteEdge, Thick), words, PortableDiagramShapes.United(build.Medium, over));
    }

    /// <summary>The notes drawn inside a namespace, which are the ones about the classes it holds.</summary>
    private static IEnumerable<Pinned> Noting(Plan plan, string? space) =>
        plan.Notes.Where(note => string.Equals(note.Space, space, StringComparison.Ordinal));

    private static IEnumerable<Sized> Inside(Diagram diagram, Plan plan, string? space) =>
        diagram.Inside(space)
            .Select(node => plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)))
            .OfType<Sized>();

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>What a class is filled with: what its styling writes, and otherwise what every class is.</summary>
    private LayoutInk Fill(Node node)
    {
        var fill = Ink.Written(node.Style.Fill) ?? Ink.Node;

        return node.Style.FillOpacity is { } opacity ? PortableDiagramInk.Faded(Medium, fill, opacity) : fill;
    }

    /// <summary>What a class is outlined in: what its styling writes, and otherwise what every class is.</summary>
    private PortableDiagramStroke Stroke(MermaidStyle style) =>
        new(Ink.Written(style.Stroke) ?? Ink.NodeEdge, style.StrokeWidth ?? Thick, PortableDiagramInk.Dashes(style.Dashes));

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>A class measured: the words of each band, the box they are set in, and the cell the layout placed it in.</summary>
    private sealed class Sized(
        Node node,
        IReadOnlyList<(Member Member, PortableDiagramWords Words)> fielded,
        IReadOnlyList<(Member Member, PortableDiagramWords Words)> methoded,
        IReadOnlyList<(Lollipop Lollipop, PortableDiagramWords Words)> offered,
        PortableDiagramBox laid)
    {
        public Node Node { get; } = node;

        public IReadOnlyList<(Member Member, PortableDiagramWords Words)> Fielded { get; } = fielded;

        public IReadOnlyList<(Member Member, PortableDiagramWords Words)> Methoded { get; } = methoded;

        public IReadOnlyList<(Lollipop Lollipop, PortableDiagramWords Words)> Offered { get; } = offered;

        /// <summary>Its name and its members measured into a box of compartments (<see cref="PortableDiagramBox"/>).</summary>
        public PortableDiagramBox Laid { get; } = laid;

        /// <summary>The room kept above and below the box for the interfaces it offers.</summary>
        public double Above { get; init; }

        public double Below { get; init; }

        /// <summary>How wide and how deep the box itself is, which is less than the cell where lollipops widen it.</summary>
        public double Wide => Laid.Size.Width;

        public double Deep => Laid.Size.Height;

        public LayoutSize Size { get; init; }

        public PortableDiagramCell Cell { get; set; } = new(default);
    }

    /// <summary>A namespace measured.</summary>
    private sealed class Box(Namespace space, IReadOnlyList<PortableDiagramWords> words)
    {
        public Namespace Space { get; } = space;

        public IReadOnlyList<PortableDiagramWords> Words { get; } = words;

        public required PortableDiagramCell Cell { get; init; }
    }

    /// <summary>A note measured, held in the rank of the class it is about and drawn inside whatever holds that class.</summary>
    private sealed class Pinned(Note note, IReadOnlyList<PortableDiagramWords> words, string? space)
    {
        public Note Note { get; } = note;

        public IReadOnlyList<PortableDiagramWords> Words { get; } = words;

        /// <summary>The namespace the class it is about is in, or null for one about a class outside them all.</summary>
        public string? Space { get; } = space;

        public required PortableDiagramCell Cell { get; init; }

        /// <summary>What holds the note beside the class it is about, which the note's tether is drawn along — null where no class is called that.</summary>
        public PortableDiagramJoin? Join { get; set; }
    }

    /// <summary>A relation worked out: where it runs, what is written on it, and the counts at either end.</summary>
    private sealed record Route(Relation Relation, IReadOnlyList<LayoutPoint> Along, IReadOnlyList<PortableDiagramWords> Said, LayoutRect Room)
    {
        public PortableDiagramWords? Near { get; init; }

        public PortableDiagramWords? Far { get; init; }
    }

    /// <summary>Everything the diagram was measured and laid out into.</summary>
    private sealed class Plan
    {
        public List<Sized> Nodes { get; } = [];

        public List<Pinned> Notes { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Box> Spaces { get; } = new(StringComparer.Ordinal);

        public Dictionary<Relation, PortableDiagramJoin> Joins { get; } = [];

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public PortableDiagramSpill Spill { get; set; } = PortableDiagramSpill.None;

        /// <summary>The joins that hold a note beside the class it is about, which nothing draws.</summary>
        public List<PortableDiagramJoin> Beside { get; } = [];

        public LayoutSize Size { get; set; }
    }
}