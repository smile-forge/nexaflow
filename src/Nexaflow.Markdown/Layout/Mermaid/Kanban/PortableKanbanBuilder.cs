using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Kanban;

/// <summary>
/// Draws a <c>kanban</c> block as a board: its columns side by side as lanes, each as wide as <c>sectionWidth</c> and all as
/// tall as the longest, washed in the column's colour under a deeper heading that holds its title and how many cards it has;
/// each card a raised box in its lane, a stripe down its left edge in its priority's colour (or its column's, where it has no
/// priority), its title wrapped at its top, and under it a chip each for its ticket, its priority and who it is assigned to.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A column and a card stand for their lines, and their titles are
/// the characters written, typed into where they are drawn, line by line where they wrap. A ticket links where the front matter's
/// <c>ticketBaseUrl</c> says, and is drawn in the accent where it does.
/// </para>
/// </summary>
internal sealed class PortableKanbanBuilder : PortableContentBuilder
{
    private const double SectionWidth = 230;
    private const double TitleSize = 14;
    private const double CountSize = 12;
    private const double CardSize = 13;
    private const double ChipSize = 11;

    /// <summary>How far apart the columns stand, how far in from its lane's edges a card stands, and how far apart the cards.</summary>
    private const double Apart = 10;
    private const double Margin = 8;

    /// <summary>The air above and below a column's heading, round a card's words, and beside them.</summary>
    private const double HeadingPad = 8;
    private const double CardPad = 8;
    private const double Padding = 9;

    /// <summary>How wide a card's stripe is, and how round the lanes' and cards' corners.</summary>
    private const double Stripe = 4;
    private const double Corner = 5;

    /// <summary>The air inside a chip beside its words, between chips, and how round their corners.</summary>
    private const double ChipPad = 5;
    private const double ChipGap = 5;
    private const double ChipCorner = 3;

    /// <summary>How strongly a lane is washed in its column's colour, its heading deeper, and a chip in its own.</summary>
    private const double LaneWash = 0.07;
    private const double HeadingWash = 0.22;
    private const double ChipWash = 0.18;

    /// <summary>How tall a lane is with no cards in it, at least.</summary>
    private const double Least = 50;

    /// <summary>How much bigger a card's icon is drawn than its title.</summary>
    private const double IconScale = 1.3;

    internal PortableKanbanBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                   PortableHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new PortableDiagramInk(style, medium);
        Words = new PortableDiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, PortableStanding.Inline));
        Sheet = new PortableDiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private PortableDiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private PortableDiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private PortableDiagramSheet Sheet { get; }

    protected override PortableLaid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new PortableBuilder(Medium);
        var body = Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten), null, null);
    }

    protected override PortableCharacters Characters => PortableDiagramSheet.Characters(Style);

    /// <summary>A column: the part of the reading it is, what its stage said of it, and the cards in its lane.</summary>
    private readonly record struct Column(ContentPart Part, KanbanColumnNode Said, List<Item> Cards);

    /// <summary>A card: the part of the reading it is, what its stage said of it, and the icon line under it where one is.</summary>
    private readonly record struct Item(ContentPart Part, KanbanCardNode Said, ContentPart? Icon = null);

    private LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        // Each card goes in the lane of the column written above it, and an icon line names the icon of the card above it.
        var columns = new List<Column>();
        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Node)
            {
                case KanbanColumnNode { Trouble: null } column:
                    columns.Add(new Column(part, column, []));
                    break;

                case KanbanCardNode { Trouble: null } written when columns.Count > 0:
                    columns[^1].Cards.Add(new Item(part, written));
                    break;

                case { Kind: KanbanKinds.Icon } when columns.Count > 0 && columns[^1].Cards.Count > 0:
                    columns[^1].Cards[^1] = columns[^1].Cards[^1] with { Icon = part };
                    break;
            }
        }

        // A board with no column is the source.
        if (columns.Count == 0) return Sheet.AsWritten();

        var config = PortableDiagramConfig.Of(Reading, KanbanConfig.Default);
        var width = config.SectionWidth ?? SectionWidth;
        var card = width - (2 * Margin);
        var text = Ink.Written(config.TextColour) ?? Style.Text;

        // Every column's heading first — its title at the left and how many cards it holds at the right — so that all of them
        // are as tall as the tallest, and the first cards of every column stand level.
        var headings = columns.Select((column, index) =>
        {
            var count = Words.Worked(column.Cards.Count.ToString(CultureInfo.InvariantCulture), column.Part, CountSize, Style.TextMuted);
            var title = Titled(Title(column.Part), HoleOf(column.Part), column.Said.Label, column.Part, TitleSize,
                               Ink.Written(config.ScaleLabel.GetValueOrDefault(Scale(index))) ?? text, width - (2 * Margin) - count.Width - Margin, 600);
            return (Title: title, Count: count);
        }).ToList();
        var heading = headings.Max(said => Math.Max(said.Title.Sum(line => line.Height), said.Count.Height)) + (2 * HeadingPad);

        var laid = new List<(Column Column, double Left, List<Laying> Cards)>();
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var left = index * (width + Apart);
            var cards = new List<Laying>();
            var y = heading + Margin;

            foreach (var item in column.Cards)
            {
                var inner = card - Stripe - (2 * Padding);
                var said = item.Said;
                // The icon it names — on the line under it, or in its metadata — goes before its title.
                var icon = Words.Iconed(item.Icon ?? item.Part, CardSize * IconScale, text);
                var before = icon is null ? 0 : icon.Width + ChipGap;
                var lines = Titled(Title(item.Part), HoleOf(item.Part), said.Label, item.Part, CardSize, text, inner - before, null);
                var titleHeight = Math.Max(lines.Sum(line => line.Height), icon?.Height ?? 0);
                var bounds = new LayoutRect(left + Margin, y, card, 0);
                var words = new List<(PortableDiagramWords, LayoutPoint, string)>();
                if (icon is not null) words.Add((icon, new LayoutPoint(bounds.Left + Stripe + Padding, y + CardPad), MermaidPiece.Glyph));
                words.AddRange(PortableDiagramWords.Placed(lines, new LayoutRect(bounds.Left + Stripe + Padding + before, y + CardPad, inner - before, titleHeight), KanbanPiece.Title, LayoutAlign.Left));

                // Under the title, a chip each for its ticket, its priority and who it is assigned to, running on to another row
                // where they do not all fit.
                var chips = new List<Chip>();
                if (said.Ticket is { } ticket) chips.Add(new Chip(Words.Worked(ticket, item.Part, ChipSize, said.Linked ? Style.Accent : text), Style.Accent, KanbanPiece.Ticket));
                if (Priority(said.Priority) is { } urgency) chips.Add(new Chip(Words.Worked(said.Priority!, item.Part, ChipSize, text), urgency, MermaidPiece.Words));
                if (said.Assigned is { } who) chips.Add(new Chip(Words.Worked(who, item.Part, ChipSize, text), Style.TextMuted, KanbanPiece.Assigned));

                var foot = y + CardPad + titleHeight;
                if (chips.Count > 0)
                {
                    var (x, row) = (bounds.Left + Stripe + Padding, foot + ChipGap);
                    foreach (var chip in chips)
                    {
                        var size = new LayoutSize(chip.Words.Width + (2 * ChipPad), chip.Words.Height + 2);
                        if (x + size.Width > bounds.Right - Padding && x > bounds.Left + Stripe + Padding)
                            (x, row) = (bounds.Left + Stripe + Padding, row + size.Height + ChipGap);

                        chip.Box = new LayoutRect(new LayoutPoint(x, row), size);
                        x += size.Width + ChipGap;
                    }

                    foot = chips.Max(chip => chip.Box.Bottom);
                }

                bounds = new LayoutRect(bounds.X, bounds.Y, bounds.Width, foot + CardPad - y);
                cards.Add(new Laying(item, bounds, words, chips));
                y = bounds.Bottom + Margin;
            }

            laid.Add((column, left, cards));
        }

        // Every column runs as far down as the longest, as the lanes of a board do.
        var tall = Math.Max(heading + Least, laid.Max(entry => entry.Cards.Select(laying => laying.Bounds.Bottom).DefaultIfEmpty(heading).Max()) + Margin);

        for (var index = 0; index < laid.Count; index++)
        {
            var (column, left, cards) = laid[index];
            var accent = Ink.Written(config.Scale.GetValueOrDefault(Scale(index))) ?? Ink.Series(index);
            var lane = new LayoutRect(left, 0, width, tall);
            var (title, count) = headings[index];

            var said = PortableDiagramWords.Placed(title, new LayoutRect(left + Margin, HeadingPad, width - (2 * Margin) - count.Width - Margin, title.Sum(line => line.Height)), KanbanPiece.Title, LayoutAlign.Left).ToList();
            said.Add((count, new LayoutPoint(lane.Right - Margin - count.Width, (heading - count.Height) / 2), MermaidPiece.Words));

            build.Open(KanbanPiece.Column, column.Part, stops: PortableStops.None);
            Lane(build, column.Part, lane, heading, accent, cards, said);
            foreach (var laying in cards) Card(build, config, laying, accent);
            build.Close();
        }

        return new LayoutSize(laid[^1].Left + width, tall);
    }

    /// <summary>
    /// A column's lane: washed in its colour, with its heading washed deeper across its top and ruled off under in the colour
    /// itself. The lane stands wherever its cards and words do not.
    /// </summary>
    private static void Lane(PortableBuilder build, ContentPart part, LayoutRect lane, double heading, LayoutInk accent, IReadOnlyList<Laying> cards,
                             IReadOnlyList<(PortableDiagramWords Words, LayoutPoint At, string Kind)> said)
    {
        var outline = build.Medium.Rectangle(lane, Corner, Corner);
        var top = build.Medium.Combine(outline, build.Medium.Rectangle(new LayoutRect(lane.Left, lane.Top, lane.Width, heading)), ShapeCombine.Intersect);
        var rule = build.Medium.Line(new LayoutPoint(lane.Left, lane.Top + heading), new LayoutPoint(lane.Right, lane.Top + heading));

        var over = new List<LayoutShape>();
        foreach (var laying in cards) over.Add(build.Medium.Rectangle(laying.Bounds, Corner, Corner));
        foreach (var (words, at, _) in said) over.Add(build.Medium.Rectangle(new LayoutRect(at, new LayoutSize(words.Width, words.Height))));
        var stands = build.Medium.Combine(outline, build.Medium.Group(over), ShapeCombine.Exclude);

        build.Open(MermaidPiece.Shape, part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, outline, PortableDiagramInk.Faded(build.Medium, accent, LaneWash), null, 0));
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, top, PortableDiagramInk.Faded(build.Medium, accent, HeadingWash), null, 0));
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, rule, null, accent, 2));
        build.Occupies(stands);
        build.Close();

        foreach (var (words, at, kind) in said) words.Set(build, at, kind);
    }

    /// <summary>
    /// A card: a raised box with a stripe down its left edge — its priority's colour where it has one, its column's where not —
    /// its title, and a chip for each of its ticket, its priority and who it is assigned to.
    /// </summary>
    private void Card(PortableBuilder build, KanbanConfig config, Laying laying, LayoutInk accent)
    {
        var bounds = laying.Bounds;
        var outline = build.Medium.Rectangle(bounds, Corner, Corner);
        var stripe = build.Medium.Combine(outline, build.Medium.Rectangle(new LayoutRect(bounds.Left, bounds.Top, Stripe, bounds.Height)), ShapeCombine.Intersect);
        var chips = new List<LayoutShape>();
        foreach (var chip in laying.Chips) chips.Add(build.Medium.Rectangle(chip.Box, ChipCorner, ChipCorner));

        var over = new List<LayoutShape> { build.Medium.Group(chips) };
        foreach (var (words, at, _) in laying.Words) over.Add(build.Medium.Rectangle(new LayoutRect(at, new LayoutSize(words.Width, words.Height))));
        var stands = build.Medium.Combine(outline, build.Medium.Group(over), ShapeCombine.Exclude);

        var urgency = Priority(laying.Card.Said.Priority);

        build.Open(KanbanPiece.Card, laying.Card.Part, stops: PortableStops.None);

        build.Open(MermaidPiece.Shape, laying.Card.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, outline, Ink.Written(config.Background) ?? Style.TableHeaderBg, Ink.Written(config.NodeBorder) ?? Style.CodeBorder, 1));
        if (urgency is null) build.Draw(PortableDiagramMarks.Outlined(build.Medium, stripe, accent, null, 0));
        foreach (var chip in laying.Chips)
        {
            var box = build.Medium.Rectangle(chip.Box, ChipCorner, ChipCorner);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, box, PortableDiagramInk.Faded(build.Medium, chip.Ink, ChipWash), chip.Ink, 1));
        }

        build.Occupies(stands);
        build.Close();

        // A priority stands for the card as its stripe, which is drawn in the priority's colour.
        if (urgency is not null)
        {
            build.Open(KanbanPiece.Priority, laying.Card.Part, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, stripe, urgency, null, 0));
            build.Close();
        }

        foreach (var (words, at, kind) in laying.Words) words.Set(build, at, kind);
        foreach (var chip in laying.Chips) chip.Words.Set(build, new LayoutPoint(chip.Box.Left + ChipPad, chip.Box.Top + 1), chip.Kind);

        build.Close();
    }

    /// <summary>A chip on a card: what it says, the colour it is washed and edged in, and where it went.</summary>
    private sealed record Chip(PortableDiagramWords Words, LayoutInk Ink, string Kind)
    {
        public LayoutRect Box { get; set; }
    }

    /// <summary>A card laid out: where it is, its title's words and where each goes, and its chips.</summary>
    private sealed record Laying(Item Card, LayoutRect Bounds, List<(PortableDiagramWords Words, LayoutPoint At, string Kind)> Words, IReadOnlyList<Chip> Chips);

    /// <summary>A title as written, wrapped to the room — or the label metadata gives it instead, pressed as the node.</summary>
    private IReadOnlyList<PortableDiagramWords> Titled(ContentPart? title, ContentPart? hole, string? label,
                                                       ContentPart node, double size, LayoutInk ink, double room, int? weight) =>
        label is not null ? [Words.Worked(label, node, size, ink, weight)] : Words.Wrapped(title, hole, size, ink, room, weight);

    /// <summary>A node's title: the words in its brackets, or else its bare id — null where it is still to write.</summary>
    private static ContentPart? Title(ContentPart node) =>
        node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Words() is { Length: > 0 } label ? label
        : node.Children.Any(child => child.Kind == MermaidKinds.Label) ? null
        : node.Children.FirstOrDefault(child => child is { Kind: MermaidKinds.Name, Role: KanbanRoles.Id }).Words() is { Length: > 0 } id ? id
        : null;

    /// <summary>The hole standing where a node's title is still to write.</summary>
    private static ContentPart? HoleOf(ContentPart node) => node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Hole();

    /// <summary>Which of the theme's scale a column takes: Mermaid numbers its columns from one, and styles the first with the scale's third.</summary>
    private static int Scale(int column) => (column + 2) % 12;

    /// <summary>The ink of a priority's stripe — none for <c>Medium</c>, or for a priority Mermaid does not know.</summary>
    private LayoutInk? Priority(string? priority) => priority switch
    {
        "Very High" => Style.Danger,
        "High" => Style.Warning,
        "Low" => Style.Accent,
        "Very Low" => PortableDiagramInk.Faded(Medium, Style.Accent, 0.5),
        _ => null,
    };
}
