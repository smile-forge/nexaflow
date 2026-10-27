using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Cynefin;

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
internal sealed class CynefinBuilder : ContentBuilder
{
    /// <summary>The least a domain's cell is drawn at, before what is in it asks for more.</summary>
    private const double Least = 210;

    private const double Pad = 12;
    private const double Gap = 6;
    private const double Snug = 4;

    /// <summary>The clear air round the words in a card, and between a movement's line and its label.</summary>
    private const double Air = 6;
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

    /// <summary>
    /// How each domain is worked, which it says under its name: the decision model it asks for, and the kind of practice that
    /// comes of it — as the Cynefin framework names them, in the window's language. Disorder is where a thing sits while nobody knows.
    /// </summary>
    private IReadOnlyList<string> Practices(CynefinDomain domain) => domain switch
    {
        CynefinDomain.Clear => [Style.Says("Text.Cynefin.Clear.Model"), Style.Says("Text.Cynefin.Clear.Practice")],
        CynefinDomain.Complicated => [Style.Says("Text.Cynefin.Complicated.Model"), Style.Says("Text.Cynefin.Complicated.Practice")],
        CynefinDomain.Complex => [Style.Says("Text.Cynefin.Complex.Model"), Style.Says("Text.Cynefin.Complex.Practice")],
        CynefinDomain.Chaotic => [Style.Says("Text.Cynefin.Chaotic.Model"), Style.Says("Text.Cynefin.Chaotic.Practice")],
        _ => [Style.Says("Text.Cynefin.Disorder")],
    };

    internal CynefinBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                    LayoutHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private DiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private DiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private DiagramSheet Sheet { get; }

    protected override Laid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new LayoutBuilder(Medium);
        var body = Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten), null, null);
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>The five sense-making domains, in the order their words are listed (<see cref="CynefinGrammar.Domains"/>).</summary>
    private enum CynefinDomain { Clear, Complicated, Complex, Chaotic, Confusion }

    /// <summary>Text somebody wrote: the line it was written on — what pressing it means — what it says, without its quotes, and the hole standing where it is still to write.</summary>
    private readonly record struct Phrase(ContentPart Part, ContentPart Says, ContentPart? Hole);

    /// <summary>Where a domain is opened: the line, and the word opening it — which is what is typed into where it is named.</summary>
    private sealed record Opening(ContentPart Part, ContentPart Word);

    /// <summary>One item: the item as written — what pressing it means — and what it says.</summary>
    private sealed record Item(ContentPart Part, Phrase Says);

    /// <summary>A movement from one domain to another — or to none where that is still to write — with what it says where anything does.</summary>
    private sealed record Move(ContentPart Part, CynefinDomain From, CynefinDomain? To, Phrase? Label);

    /// <summary>
    /// What the block says, read in the order it is written: where each domain is opened, the items sitting in each, and the
    /// movements between them. A domain opened twice is the one domain, opened where it is first written, and its items stay
    /// in the order they are written. An item is in the domain opened above it; one written before any is opened is nowhere to
    /// draw, which its stage says where it is written.
    /// </summary>
    private sealed class Grid(CynefinConfig config)
    {
        private readonly Dictionary<CynefinDomain, Opening> opened = [];
        private readonly Dictionary<CynefinDomain, List<Item>> items = [];

        public CynefinConfig Config { get; } = config;

        public List<Move> Moves { get; } = [];

        /// <summary>Whether nothing is written for the diagram to draw.</summary>
        public bool Empty => opened.Count == 0 && items.Count == 0 && Moves.Count == 0;

        /// <summary>Where a domain is opened — what pressing it means — or null for a domain nothing opens.</summary>
        public Opening? Opened(CynefinDomain domain) => opened.GetValueOrDefault(domain);

        /// <summary>The items sitting in a domain, in the order they are written.</summary>
        public IReadOnlyList<Item> ItemsIn(CynefinDomain domain) => items.GetValueOrDefault(domain) ?? [];

        public void Open(CynefinDomain domain, Opening opening) => opened.TryAdd(domain, opening);

        public void Place(CynefinDomain domain, Item item)
        {
            if (!items.TryGetValue(domain, out var placed)) items[domain] = placed = [];
            placed.Add(item);
        }
    }

    private Grid Read()
    {
        var grid = new Grid(DiagramConfig.Of(Reading, CynefinConfig.Default));
        CynefinDomain? above = null;

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case CynefinKinds.Domain when Key(part, 0) is { } word && Domain(word.Text) is { } domain:
                    grid.Open(domain, new Opening(part, word));
                    above = domain;
                    break;

                case CynefinKinds.Item when above is { } domain && Text(part, CynefinRoles.Says) is { } says:
                    grid.Place(domain, new Item(part, says));
                    break;

                case CynefinKinds.Move when Domain(Key(part, 0)?.Text) is { } from:
                    grid.Moves.Add(new Move(part, from, Domain(Key(part, 1)?.Text), Text(part, CynefinRoles.Label)));
                    break;
            }
        }

        return grid;
    }

    /// <summary>The domain a word opens, whatever case it is written in — or null for a word that opens none.</summary>
    private static CynefinDomain? Domain(string? word)
    {
        var said = word.AsSpan().Trim();

        for (var at = 0; at < CynefinGrammar.Domains.Count; at++)
            if (said.Equals(CynefinGrammar.Domains[at], StringComparison.OrdinalIgnoreCase)) return (CynefinDomain)at;

        return null;
    }

    /// <summary>The word a line names, of the ones it names — a domain's, or a transition's ends in the order written.</summary>
    private static ContentPart? Key(ContentPart line, int which) =>
        line.Children.Where(child => child.Kind == MermaidKinds.Key).ElementAtOrDefault(which);

    private static Phrase? Text(ContentPart line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == CynefinKinds.Text && child.Role == role) is { } text && text.Words() is { } says
            ? new Phrase(line, says, text.Hole())
            : null;

    // ── Laying it out ───────────────────────────────────────────────────────

    private LayoutSize Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = Read();

        // A diagram with nothing written in it is the source.
        if (diagram.Empty) return Sheet.AsWritten();

        var config = diagram.Config;
        var pad = config.Padding ?? Pad;
        var wrap = double.IsInfinity(Sheet.Space) ? Widest : Math.Clamp((Sheet.Space / 2) - (pad * 2) - Round, Narrowest, Widest);

        var stacks = Corners.ToDictionary(corner => corner.Domain, corner => Stacked(diagram, corner.Domain, wrap));
        var disorder = Lines(Stacked(diagram, CynefinDomain.Confusion, Math.Min(wrap, Middling)));

        // The cloud in the middle is as big as what is in it, and every cell keeps clear of the half of it reaching into that corner.
        var cloud = disorder.Count == 0
            ? default
            : DiagramShapes.Around(DiagramShape.Cloud, new LayoutSize(disorder.Max(line => line.Words.Width), disorder.Sum(line => line.Words.Height)), Snug * 2);

        var wide = Math.Max(Math.Max(Least, (config.Width ?? 0) / 2), stacks.Values.Max(stack => stack.Taken.Width) + (pad * 2) + (cloud.Width / 2));
        var tall = Math.Max(Math.Max(Least, (config.Height ?? 0) / 2), stacks.Values.Max(stack => stack.Taken.Height) + (pad * 2) + (cloud.Height / 2));

        var plot = new LayoutRect(0, 0, wide * 2, tall * 2);
        var middle = new LayoutPoint(wide, tall);
        var blob = new LayoutRect(middle.X - (cloud.Width / 2), middle.Y - (cloud.Height / 2), cloud.Width, cloud.Height);

        var cells = new Dictionary<CynefinDomain, LayoutRect>();
        var words = new List<(DiagramWords Words, LayoutPoint At, string Kind)>();
        var cards = new List<(Card Card, LayoutRect Bounds, bool Left)>();

        // Each domain's words and cards stack from its outer corner inwards, which is what keeps them clear of the middle.
        foreach (var (domain, left, top) in Corners)
        {
            var cell = new LayoutRect(left ? plot.Left : middle.X, top ? plot.Top : middle.Y, wide, tall);
            cells[domain] = cell;

            var box = LayoutRect.Inflate(cell, -pad, -pad);
            var stack = stacks[domain];
            var y = top ? box.Top : box.Bottom;

            LayoutPoint Place(LayoutSize size, double after)
            {
                var at = new LayoutPoint(left ? box.Left : box.Right - size.Width, top ? y : y - size.Height);
                y += top ? size.Height + after : -(size.Height + after);
                return at;
            }

            if (stack.Name is { } name) words.Add((name, Place(new LayoutSize(name.Width, name.Height), stack.About.Count == 0 ? Gap : Snug), CynefinPiece.Name));

            // Stacking up from the foot of the grid puts the last line first, so what it says stays in the order it is read in.
            var said = top ? stack.About : [.. stack.About.Reverse()];

            for (var line = 0; line < said.Count; line++)
            {
                var about = said[line];
                words.Add((about, Place(new LayoutSize(about.Width, about.Height), line == said.Count - 1 ? Gap : 0), CynefinPiece.About));
            }
            foreach (var card in stack.Cards) cards.Add((card, new LayoutRect(Place(card.Size, Gap), card.Size), left));
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
        var over = new List<LayoutShape>();
        foreach (var (said, at, _) in words) over.Add(build.Medium.Rectangle(new LayoutRect(at + shift, new LayoutSize(said.Width, said.Height))));
        foreach (var (_, bounds, _) in cards) over.Add(build.Medium.Rectangle(LayoutRect.Offset(bounds, shift)));
        if (cloud.Width > 0) over.Add(DiagramShapes.Outline(build.Medium, DiagramShape.Cloud, LayoutRect.Offset(blob, shift)));
        foreach (var (_, route) in routes) over.Add(DiagramConnector.Band(build.Medium, [.. route.Select(at => at + shift)], Thick, curved: route.Count > 2));

        Domains(build, diagram, cells, shift, build.Medium.Group(over));
        Boundaries(build, config, plot, blob, cloud, shift);
        Moves(build, config, routes, shift);
        Centre(build, diagram, disorder, blob, cloud, shift);
        Cards(build, cards, shift);

        build.Open(CynefinPiece.Words, part: null, stops: LayoutStops.None);
        foreach (var (said, at, kind) in words) said.Set(build, at + shift, kind);
        build.Close();

        return room.Size;
    }

    // ── What a domain shows ─────────────────────────────────────────────────

    /// <summary>One item, its words wrapped, and the card drawn round them.</summary>
    private sealed record Card(Item Item, IReadOnlyList<DiagramWords> Lines, LayoutSize Size);

    /// <summary>What a domain shows, stacked from its outer corner: its word, how it is worked, and a card for each item in it.</summary>
    private sealed record Sized(DiagramWords? Name, IReadOnlyList<DiagramWords> About, IReadOnlyList<Card> Cards)
    {
        /// <summary>How much room it all takes, the gaps between included.</summary>
        public LayoutSize Taken => new(
            Math.Max(Math.Max(Name?.Width ?? 0, DiagramWords.Taken(About).Width), Cards.Select(card => card.Size.Width).DefaultIfEmpty(0).Max()),
            (Name?.Height ?? 0) + (About.Count == 0 ? 0 : DiagramWords.Taken(About).Height + Snug) + Cards.Sum(card => card.Size.Height + Gap));
    }

    private Sized Stacked(Grid diagram, CynefinDomain domain, double wrap)
    {
        var config = diagram.Config;
        var items = diagram.ItemsIn(domain);
        var opened = diagram.Opened(domain);

        var ink = Ink.Written(config.TextColour) ?? Style.Text;
        var said = config.ItemFontSize ?? ItemSize;

        var name = opened is null
            ? null
            : Words.Written(opened.Word, hole: null, config.DomainFontSize ?? NameSize, Ink.Written(config.LabelColour) ?? Style.Text, 600);

        // How the domain is worked, in the words the framework uses for it: the decision model, then the practice.
        var about = config.ShowDomainDescriptions && (opened is not null || items.Count > 0)
            ? Practices(domain)
                .Select(says => Words.Worked(says, opened?.Part, config.ItemFontSize ?? AboutSize, Ink.Written(config.TextColour) ?? Style.TextMuted))
                .ToList()
            : [];

        var cards = items.Select(item =>
        {
            var lines = Words.Wrapped(item.Says.Says, item.Says.Hole, said, ink, wrap);
            return new Card(item, lines, DiagramShapes.Around(DiagramShape.Rounded, DiagramWords.Taken(lines), Air));
        });

        return new Sized(name, about, [.. cards]);
    }

    /// <summary>What disorder shows, line by line: its word, how it is worked, and what each item in it says.</summary>
    private static IReadOnlyList<(DiagramWords Words, string Kind)> Lines(Sized shown)
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
    private IReadOnlyList<(Move Move, IReadOnlyList<LayoutPoint> Route)> Routes(Grid diagram, IReadOnlyDictionary<CynefinDomain, LayoutRect> cells,
        LayoutPoint middle, LayoutRect blob, LayoutSize cloud, List<(DiagramWords Words, LayoutPoint At, string Kind)> words)
    {
        var routes = new List<(Move Move, IReadOnlyList<LayoutPoint> Route)>();

        foreach (var move in diagram.Moves)
        {
            if (move.To is not { } to || to == move.From) continue;

            var from = Anchor(move.From, Anchor(to, middle));
            var at = Anchor(to, from);
            var route = Bent(from, at);

            routes.Add((move, route));

            if (Said(move.Label, LabelSize, Ink.Written(diagram.Config.TextColour) ?? Style.Text) is { } label)
            {
                // Beside the line rather than under it, square to the way it runs, so the line never crosses what it says.
                var along = route[1] - from;
                along = along.Normalized();

                var off = new LayoutVector(along.Y, -along.X) * ((label.Height / 2) + Beside);
                var beside = from + ((route[1] - from) * Along) + off;

                words.Add((label, new LayoutPoint(beside.X - (label.Width / 2), beside.Y - (label.Height / 2)), CynefinPiece.Label));
            }
        }

        return routes;

        LayoutPoint Anchor(CynefinDomain domain, LayoutPoint toward) =>
            cells.TryGetValue(domain, out var cell) ? new LayoutPoint(cell.Left + (cell.Width / 2), cell.Top + (cell.Height / 2))
            : cloud.Width > 0 ? DiagramShapes.Edge(DiagramShape.Cloud, blob, toward)
            : middle;

        // A movement across the grid would run under the cloud, where nothing of it would be seen: it bends round instead.
        IReadOnlyList<LayoutPoint> Bent(LayoutPoint from, LayoutPoint at)
        {
            var clear = Math.Max(cloud.Width, cloud.Height) / 2;
            if (clear <= 0) return [from, at];

            var half = new LayoutPoint((from.X + at.X) / 2, (from.Y + at.Y) / 2);
            if ((half - middle).Length > clear) return [from, at];

            var along = at - from;
            along = along.Normalized();

            return [from, half + (new LayoutVector(along.Y, -along.X) * (clear + Round)), at];
        }
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    private void Domains(LayoutBuilder build, Grid diagram, IReadOnlyDictionary<CynefinDomain, LayoutRect> cells, LayoutVector shift, LayoutShape over)
    {
        build.Open(CynefinPiece.Domains, part: null, stops: LayoutStops.None);

        foreach (var (domain, _, _) in Corners)
        {
            var shape = build.Medium.Rectangle(LayoutRect.Offset(cells[domain], shift));

            build.Open(CynefinPiece.Domain, diagram.Opened(domain)?.Part, stops: LayoutStops.None);
            build.Draw(DiagramMarks.Outlined(build.Medium, shape, Fill(diagram, domain), null, 0));

            // A domain stands where nothing drawn over it does: a press on a card, a word or a movement means that.
            if (diagram.Opened(domain) is not null)
            {
                var stands = build.Medium.Combine(shape, over, ShapeCombine.Exclude);
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
    private void Boundaries(LayoutBuilder build, CynefinConfig config, LayoutRect plot, LayoutRect blob, LayoutSize cloud, LayoutVector shift)
    {
        var grid = LayoutRect.Offset(plot, shift);
        var middle = new LayoutPoint(grid.Left + (grid.Width / 2), grid.Top + (grid.Height / 2));
        var disorder = LayoutRect.Offset(blob, shift);

        var lines = new List<LayoutShape> { build.Medium.Rectangle(grid) };
        var cliff = new List<LayoutShape>();

        foreach (var (edge, falls) in new[]
                 {
                     (new LayoutPoint(middle.X, grid.Top), false),
                     (new LayoutPoint(grid.Right, middle.Y), false),
                     (new LayoutPoint(middle.X, grid.Bottom), true),
                     (new LayoutPoint(grid.Left, middle.Y), false),
                 })
        {
            var into = cloud.Width > 0 ? DiagramShapes.Edge(DiagramShape.Cloud, disorder, edge) : middle;
            var half = new LayoutPoint((edge.X + into.X) / 2, (edge.Y + into.Y) / 2);

            // Bowed square to the way it runs, every boundary the same way round: the swirl.
            var along = into - edge;
            along = along.Normalized();

            (falls ? cliff : lines).Add(DiagramCurve.Bowed(build.Medium, edge, half + (new LayoutVector(along.Y, -along.X) * Bow), into));
        }


        build.Open(CynefinPiece.Boundaries, part: null, stops: LayoutStops.None);
        build.Draw(DiagramMarks.Outlined(build.Medium, build.Medium.Group(lines), null, Ink.Written(config.BoundaryColour) ?? Style.CodeBorder, config.BoundaryWidth ?? 1));
        build.Draw(DiagramMarks.Outlined(build.Medium, build.Medium.Group(cliff), null, Ink.Written(config.CliffColour) ?? Style.Danger, config.CliffWidth ?? Falling));
        build.Close();
    }

    private void Moves(LayoutBuilder build, CynefinConfig config, IReadOnlyList<(Move Move, IReadOnlyList<LayoutPoint> Route)> routes, LayoutVector shift)
    {
        if (routes.Count == 0) return;

        var stroke = new DiagramStroke(Ink.Written(config.ArrowColour) ?? Style.TextMuted, config.ArrowWidth ?? Thick, DiagramStroke.Dashed);

        build.Open(CynefinPiece.Moves, part: null, stops: LayoutStops.None);
        foreach (var (move, route) in routes)
            DiagramConnector.Draw(build, CynefinPiece.Move, move.Part, [.. route.Select(at => at + shift)], stroke, curved: route.Count > 2);
        build.Close();
    }

    /// <summary>Disorder: a cloud in the middle, with its word and what is in it inside.</summary>
    private void Centre(LayoutBuilder build, Grid diagram, IReadOnlyList<(DiagramWords Words, string Kind)> disorder,
                        LayoutRect blob, LayoutSize cloud, LayoutVector shift)
    {
        if (cloud.Width <= 0) return;

        var bounds = LayoutRect.Offset(blob, shift);
        var placed = DiagramWords.Stack([.. disorder.Select(line => line.Words)], DiagramShapes.Inside(DiagramShape.Cloud, bounds)).ToList();
        var words = placed.Select((line, at) => (line.Words, line.At, disorder[at].Kind)).ToList();

        DiagramShapes.Draw(build, CynefinPiece.Centre, diagram.Opened(CynefinDomain.Confusion)?.Part, DiagramShape.Cloud, bounds,
            Fill(diagram, CynefinDomain.Confusion), new DiagramStroke(Ink.Written(diagram.Config.BoundaryColour) ?? Style.CodeBorder), words);
    }

    private void Cards(LayoutBuilder build, IReadOnlyList<(Card Card, LayoutRect Bounds, bool Left)> cards, LayoutVector shift)
    {
        if (cards.Count == 0) return;

        var stroke = new DiagramStroke(Style.CodeBorder);

        build.Open(CynefinPiece.Items, part: null, stops: LayoutStops.None);

        foreach (var (card, bounds, left) in cards)
        {
            var at = LayoutRect.Offset(bounds, shift);
            var inside = LayoutRect.Inflate(DiagramShapes.Inside(DiagramShape.Rounded, at), -Air, -Air);
            var words = DiagramWords.Placed(card.Lines, inside, CynefinPiece.Says, left ? LayoutAlign.Left : LayoutAlign.Right);

            DiagramShapes.Draw(build, CynefinPiece.Item, card.Item.Part, DiagramShape.Rounded, at, Style.CodeBg, stroke, words);
        }

        build.Close();
    }

    // ── Ink ─────────────────────────────────────────────────────────────────

    /// <summary>What a domain is drawn in: the colour its front matter writes, or a faded series colour of its own.</summary>
    private LayoutInk Fill(Grid diagram, CynefinDomain domain) =>
        Ink.Written(diagram.Config.DomainFills[(int)domain]) ?? DiagramInk.Faded(Medium, Ink.Series(Series(domain)), Tint);

    /// <summary>The series colour each domain takes, a hue apart: disorder pink, and the practised domains round it.</summary>
    private static int Series(CynefinDomain domain) => domain switch
    {
        CynefinDomain.Complex => 4,
        CynefinDomain.Complicated => 0,
        CynefinDomain.Clear => 2,
        CynefinDomain.Chaotic => 1,
        _ => 7,
    };

    private DiagramWords? Said(Phrase? text, double size, LayoutInk ink) =>
        text is not { } said ? null : Words.Written(said.Says, said.Hole, size, ink);
}
