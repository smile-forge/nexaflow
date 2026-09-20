using System;
using System.Collections.Generic;
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
/// Draws a <c>kanban</c> block as Mermaid lays one out: its columns side by side, each as wide as <c>sectionWidth</c> and tall
/// enough for its cards, its title at its top; each card a rounded box across the column, its title wrapped at its top left, its
/// ticket under the title at the left and its assignee at the right, and a stripe down its left edge coloured by its priority.
/// Where Mermaid's arithmetic sets a card's ticket and assignee against its edges, they are set inside its padding.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A column and a card stand for their lines, and their titles are
/// the characters written, typed into where they are drawn, line by line where they wrap. A ticket links where the front matter's
/// <c>ticketBaseUrl</c> says, and is drawn in the accent where it does.
/// </para>
/// </summary>
internal sealed class KanbanBuilder : MermaidBuilder<KanbanBoard>
{
    private const double SectionWidth = 200;
    private const double Padding = 10;
    private const double TitleSize = 14;
    private const double CardSize = 13;
    private const double Stripe = 4;

    /// <summary>How tall a column's title is given room for, at least.</summary>
    private const double TitleRoom = 25;

    private KanbanBuilder(EditState state, DiagramLaying laying) : base(state, laying) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(EditState state, DiagramLaying laying) => new KanbanBuilder(state, laying).Lay();

    /// <inheritdoc/>
    protected override KanbanBoard Of(MermaidBlock block) => KanbanBoard.Of(block);

    protected override Size Draw(KanbanBoard board, LayoutBuilder build)
    {
        // A board with no column is the source.
        if (board.Columns.Count == 0) return AsWritten(build);

        var config = board.Config;
        var width = config.SectionWidth ?? SectionWidth;
        var card = width - (1.5 * Padding);
        var text = Ink.Written(config.TextColour) ?? Palette.Text;

        var titles = board.Columns.Select((column, index) => Titled(column.Title, column.Hole, column.Label, column.Part, TitleSize, Ink.Written(config.ScaleLabel.GetValueOrDefault(Scale(index))) ?? Palette.Text, width - Padding, FontWeights.SemiBold)).ToList();
        var titleRoom = Math.Max(TitleRoom, titles.Max(lines => lines.Sum(line => line.Height)));

        var laid = new List<(KanbanColumn Column, Rect Bounds, IReadOnlyList<(DiagramWords Words, Point At, string Kind)> Title, List<Laying> Cards)>();
        for (var index = 0; index < board.Columns.Count; index++)
        {
            var column = board.Columns[index];
            var left = index * (width + (Padding / 2));
            var cards = new List<Laying>();
            var y = titleRoom;

            foreach (var item in column.Cards)
            {
                var padding = Bracketed(item) ? 2 * Padding : Padding;
                var lines = Titled(item.Title, item.Hole, item.Label, item.Part, CardSize, text, card - padding - Padding, null);
                var titleHeight = lines.Sum(line => line.Height);
                var ticket = item.Ticket is { } said ? Worked(said, item.Part, CardSize, item.Link is null ? text : Palette.Accent) : null;
                var assigned = item.Assigned is { } who ? Worked(who, item.Part, CardSize, text) : null;
                var footing = Math.Max(ticket?.Height ?? 0, assigned?.Height ?? 0);

                // The title at the top, and under it the ticket at the left and the assignee at the right.
                var bounds = new Rect(left + ((width - card) / 2), y, card, titleHeight + footing + (2 * Padding));
                var words = new List<(DiagramWords, Point, string)>();

                var room = new Rect(bounds.Left + padding, bounds.Top + Padding, bounds.Width - padding - Padding, titleHeight);
                words.AddRange(DiagramWords.Placed(lines, room, KanbanPiece.Title, TextAlignment.Left));

                var foot = room.Bottom;
                if (ticket is not null) words.Add((ticket, new Point(bounds.Left + padding, foot), KanbanPiece.Ticket));
                if (assigned is not null) words.Add((assigned, new Point(bounds.Right - Padding - assigned.Width, foot), KanbanPiece.Assigned));

                cards.Add(new Laying(item, bounds, words));
                y = bounds.Bottom + (Padding / 2);
            }

            var height = Math.Max(y - titleRoom + (3 * Padding), 50) + (titleRoom - TitleRoom);
            var title = DiagramWords.Placed(titles[index], new Rect(left, 0, width, titles[index].Sum(line => line.Height)), KanbanPiece.Title);

            laid.Add((column, new Rect(left, 0, width, Math.Max(height, titleRoom + Padding)), title, cards));
        }

        foreach (var (column, bounds, title, cards) in laid)
        {
            var index = laid.FindIndex(entry => entry.Column == column);
            var fill = Ink.Written(config.Scale.GetValueOrDefault(Scale(index))) ?? DiagramInk.Faded(Ink.Series(index), 0.18);

            var covered = new GeometryGroup();
            foreach (var laying in cards) covered.Children.Add(DiagramShapes.Outline(DiagramShape.Rounded, laying.Bounds));
            covered.Freeze();

            build.Open(KanbanPiece.Column, column.Part, stops: Stops.None);
            DiagramShapes.Draw(build, MermaidPiece.Shape, column.Part, DiagramShape.Rounded, bounds, fill, new DiagramStroke(fill), title, covered);

            foreach (var laying in cards)
            {
                DiagramShapes.Draw(build, KanbanPiece.Card, laying.Card.Part, DiagramShape.Rounded, laying.Bounds,
                                   Ink.Written(config.Background) ?? Palette.CodeBg, new DiagramStroke(Ink.Written(config.NodeBorder) ?? Palette.CodeBorder),
                                   laying.Words);

                if (Priority(laying.Card.Priority) is { } ink)
                {
                    var stripe = new LineGeometry(new Point(laying.Bounds.Left + 2, laying.Bounds.Top + 2), new Point(laying.Bounds.Left + 2, laying.Bounds.Bottom - 2));
                    stripe.Freeze();

                    build.Open(KanbanPiece.Priority, laying.Card.Part, stops: Stops.None);
                    build.Draw(new GeometryMark(stripe, null, ink, Stripe));
                    build.Close();
                }
            }

            build.Close();
        }

        return new Size(laid[^1].Bounds.Right, laid.Max(entry => entry.Bounds.Bottom));
    }

    /// <summary>A card laid out: where it is, and its words and where each goes.</summary>
    private sealed record Laying(KanbanCard Card, Rect Bounds, List<(DiagramWords Words, Point At, string Kind)> Words);

    /// <summary>A title as written, wrapped to the room — or the label metadata gives it instead, pressed as the node.</summary>
    private IReadOnlyList<DiagramWords> Titled(ContentPart? title, ContentPart? hole, string? label,
                                               ContentPart node, double size, Brush ink, double room, FontWeight? weight) =>
        label is not null ? [Worked(label, node, size, ink, weight)] : Wrapped(title, hole, size, ink, room, weight);

    /// <summary>Which of the theme's scale a column takes: Mermaid numbers its columns from one, and styles the first with the scale's third.</summary>
    private static int Scale(int column) => (column + 2) % 12;

    /// <summary>Whether a card's title is in brackets Mermaid pads twice as far: <c>[…]</c>, <c>(…)</c> or <c>{{…}}</c>.</summary>
    private static bool Bracketed(KanbanCard card) =>
        MermaidOutline.Opening(card.Part) is "[" or "(" or "{{";

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
