using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Kanban;

/// <summary>The pieces a kanban board's layout is made of — its layers, and what is in them.</summary>
public static class KanbanPiece
{
    /// <summary>A column, standing for its line, with its title at its top and its cards in it.</summary>
    public const string Column = "Column";

    /// <summary>A card, standing for its line: its title, its ticket, its assignee and its priority's stripe.</summary>
    public const string Card = "Card";

    public const string Title = "Title";
    public const string Ticket = "Ticket";
    public const string Assigned = "Assigned";
    public const string Priority = "Priority";
}

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
internal sealed class KanbanBuilder : MermaidBuilder
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

    internal KanbanBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    /// <summary>The board as its stage left it on the block.</summary>
    private KanbanBlockNode? Board => Reading.Root.Node as KanbanBlockNode;

    /// <summary>A column: the part of the reading it is, what its stage said of it, and the cards in its lane.</summary>
    private readonly record struct Column(ContentPart Part, KanbanColumnNode Said, List<Item> Cards);

    /// <summary>A card: the part of the reading it is, and what its stage said of it.</summary>
    private readonly record struct Item(ContentPart Part, KanbanCardNode Said);

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        if (Board is not { } board) return AsWritten(build);

        // Each card goes in the lane of the column written above it.
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
            }
        }

        // A board with no column is the source.
        if (columns.Count == 0) return AsWritten(build);

        var config = board.Config;
        var width = config.SectionWidth ?? SectionWidth;
        var card = width - (2 * Margin);
        var text = Ink.Written(config.TextColour) ?? Palette.Text;

        // Every column's heading first — its title at the left and how many cards it holds at the right — so that all of them
        // are as tall as the tallest, and the first cards of every column stand level.
        var headings = columns.Select((column, index) =>
        {
            var count = Worked(column.Cards.Count.ToString(CultureInfo.InvariantCulture), column.Part, CountSize, Palette.TextMuted);
            var title = Titled(Title(column.Part), HoleOf(column.Part), column.Said.Label, column.Part, TitleSize,
                               Ink.Written(config.ScaleLabel.GetValueOrDefault(Scale(index))) ?? text, width - (2 * Margin) - count.Width - Margin, FontWeights.SemiBold);
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
                var lines = Titled(Title(item.Part), HoleOf(item.Part), said.Label, item.Part, CardSize, text, inner, null);
                var titleHeight = lines.Sum(line => line.Height);
                var bounds = new Rect(left + Margin, y, card, 0);
                var words = new List<(DiagramWords, Point, string)>();
                words.AddRange(DiagramWords.Placed(lines, new Rect(bounds.Left + Stripe + Padding, y + CardPad, inner, titleHeight), KanbanPiece.Title, TextAlignment.Left));

                // Under the title, a chip each for its ticket, its priority and who it is assigned to, running on to another row
                // where they do not all fit.
                var chips = new List<Chip>();
                if (said.Ticket is { } ticket) chips.Add(new Chip(Worked(ticket, item.Part, ChipSize, said.Linked ? Palette.Accent : text), Palette.Accent, KanbanPiece.Ticket));
                if (Priority(said.Priority) is { } urgency) chips.Add(new Chip(Worked(said.Priority!, item.Part, ChipSize, text), urgency, MermaidPiece.Words));
                if (said.Assigned is { } who) chips.Add(new Chip(Worked(who, item.Part, ChipSize, text), Palette.TextMuted, KanbanPiece.Assigned));

                var foot = y + CardPad + titleHeight;
                if (chips.Count > 0)
                {
                    var (x, row) = (bounds.Left + Stripe + Padding, foot + ChipGap);
                    foreach (var chip in chips)
                    {
                        var size = new Size(chip.Words.Width + (2 * ChipPad), chip.Words.Height + 2);
                        if (x + size.Width > bounds.Right - Padding && x > bounds.Left + Stripe + Padding)
                            (x, row) = (bounds.Left + Stripe + Padding, row + size.Height + ChipGap);

                        chip.Box = new Rect(new Point(x, row), size);
                        x += size.Width + ChipGap;
                    }

                    foot = chips.Max(chip => chip.Box.Bottom);
                }

                bounds.Height = foot + CardPad - y;
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
            var lane = new Rect(left, 0, width, tall);
            var (title, count) = headings[index];

            var said = DiagramWords.Placed(title, new Rect(left + Margin, HeadingPad, width - (2 * Margin) - count.Width - Margin, title.Sum(line => line.Height)), KanbanPiece.Title, TextAlignment.Left).ToList();
            said.Add((count, new Point(lane.Right - Margin - count.Width, (heading - count.Height) / 2), MermaidPiece.Words));

            build.Open(KanbanPiece.Column, column.Part, stops: Stops.None);
            Lane(build, column.Part, lane, heading, accent, cards, said);
            foreach (var laying in cards) Card(build, config, laying, accent);
            build.Close();
        }

        return new Size(laid[^1].Left + width, tall);
    }

    /// <summary>
    /// A column's lane: washed in its colour, with its heading washed deeper across its top and ruled off under in the colour
    /// itself. The lane stands wherever its cards and words do not.
    /// </summary>
    private static void Lane(LayoutBuilder build, ContentPart part, Rect lane, double heading, Brush accent, IReadOnlyList<Laying> cards,
                             IReadOnlyList<(DiagramWords Words, Point At, string Kind)> said)
    {
        var outline = new RectangleGeometry(lane, Corner, Corner);
        var top = new CombinedGeometry(GeometryCombineMode.Intersect, outline, new RectangleGeometry(new Rect(lane.Left, lane.Top, lane.Width, heading)));
        var rule = new LineGeometry(new Point(lane.Left, lane.Top + heading), new Point(lane.Right, lane.Top + heading));

        var over = new GeometryGroup();
        foreach (var laying in cards) over.Children.Add(new RectangleGeometry(laying.Bounds, Corner, Corner));
        foreach (var (words, at, _) in said) over.Children.Add(new RectangleGeometry(new Rect(at, new Size(words.Width, words.Height))));
        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, over);
        foreach (var geometry in new Geometry[] { outline, top, rule, stands }) geometry.Freeze();

        build.Open(MermaidPiece.Shape, part, stops: Stops.None);
        build.Draw(new GeometryMark(outline, DiagramInk.Faded(accent, LaneWash), null, 0));
        build.Draw(new GeometryMark(top, DiagramInk.Faded(accent, HeadingWash), null, 0));
        build.Draw(new GeometryMark(rule, null, accent, 2));
        build.Occupies(stands);
        build.Close();

        foreach (var (words, at, kind) in said) words.Set(build, at, kind);
    }

    /// <summary>
    /// A card: a raised box with a stripe down its left edge — its priority's colour where it has one, its column's where not —
    /// its title, and a chip for each of its ticket, its priority and who it is assigned to.
    /// </summary>
    private void Card(LayoutBuilder build, KanbanConfig config, Laying laying, Brush accent)
    {
        var bounds = laying.Bounds;
        var outline = new RectangleGeometry(bounds, Corner, Corner);
        var stripe = new CombinedGeometry(GeometryCombineMode.Intersect, outline, new RectangleGeometry(new Rect(bounds.Left, bounds.Top, Stripe, bounds.Height)));
        var chips = new GeometryGroup();
        foreach (var chip in laying.Chips) chips.Children.Add(new RectangleGeometry(chip.Box, ChipCorner, ChipCorner));

        var over = new GeometryGroup { Children = { chips } };
        foreach (var (words, at, _) in laying.Words) over.Children.Add(new RectangleGeometry(new Rect(at, new Size(words.Width, words.Height))));
        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, over);
        foreach (var geometry in new Geometry[] { outline, stripe, stands }) geometry.Freeze();

        var urgency = Priority(laying.Card.Said.Priority);

        build.Open(KanbanPiece.Card, laying.Card.Part, stops: Stops.None);

        build.Open(MermaidPiece.Shape, laying.Card.Part, stops: Stops.None);
        build.Draw(new GeometryMark(outline, Ink.Written(config.Background) ?? Palette.TableHeaderBg, Ink.Written(config.NodeBorder) ?? Palette.CodeBorder, 1));
        if (urgency is null) build.Draw(new GeometryMark(stripe, accent, null, 0));
        foreach (var chip in laying.Chips)
        {
            var box = new RectangleGeometry(chip.Box, ChipCorner, ChipCorner);
            box.Freeze();
            build.Draw(new GeometryMark(box, DiagramInk.Faded(chip.Ink, ChipWash), chip.Ink, 1));
        }

        build.Occupies(stands);
        build.Close();

        // A priority stands for the card as its stripe, which is drawn in the priority's colour.
        if (urgency is not null)
        {
            build.Open(KanbanPiece.Priority, laying.Card.Part, stops: Stops.None);
            build.Draw(new GeometryMark(stripe, urgency, null, 0));
            build.Close();
        }

        foreach (var (words, at, kind) in laying.Words) words.Set(build, at, kind);
        foreach (var chip in laying.Chips) chip.Words.Set(build, new Point(chip.Box.Left + ChipPad, chip.Box.Top + 1), chip.Kind);

        build.Close();
    }

    /// <summary>A chip on a card: what it says, the colour it is washed and edged in, and where it went.</summary>
    private sealed record Chip(DiagramWords Words, Brush Ink, string Kind)
    {
        public Rect Box { get; set; }
    }

    /// <summary>A card laid out: where it is, its title's words and where each goes, and its chips.</summary>
    private sealed record Laying(Item Card, Rect Bounds, List<(DiagramWords Words, Point At, string Kind)> Words, IReadOnlyList<Chip> Chips);

    /// <summary>A title as written, wrapped to the room — or the label metadata gives it instead, pressed as the node.</summary>
    private IReadOnlyList<DiagramWords> Titled(ContentPart? title, ContentPart? hole, string? label,
                                               ContentPart node, double size, Brush ink, double room, FontWeight? weight) =>
        label is not null ? [Worked(label, node, size, ink, weight)] : Wrapped(title, hole, size, ink, room, weight);

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
    private Brush? Priority(string? priority) => priority switch
    {
        "Very High" => Palette.Danger,
        "High" => Palette.Warning,
        "Low" => Palette.Accent,
        "Very Low" => DiagramInk.Faded(Palette.Accent, 0.5),
        _ => null,
    };
}
