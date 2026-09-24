using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Kanban;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>kanban</c> block drawn on the shared layout tree: columns side by side standing for their lines, cards stacked in them
/// standing for theirs, and every title typed into where it is drawn — line by line where it wraps.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("kanban")]
public class KanbanBuilderTests : MermaidBuilderContract
{
    private const string Board =
        "kanban\n  Todo\n    [Create Documentation]\n    docs[Create Blog about the new diagram]\n  id10[Ready for test]\n"
        + "    id4[Create parsing tests that cover every case anybody could think of writing]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }\n"
        + "    id66[last item]@{ priority: 'Very Low', assigned: 'knsv' }";

    public override MermaidDiagram Diagram => MermaidDiagram.Kanban;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("a board with metadata", Board),
        ("every bracket, icons and classes", "kanban\n  a(Round)\n    b((Circle))\n    c{{Hexagon}}\n    d)Cloud(\n    e))Bang((\n    f[\"In &quot;quotes&quot;\"]\n    ::icon(fa fa-book)\n    :::urgent"),
        ("links, labels and a theme of its own",
            "---\nconfig:\n  kanban:\n    ticketBaseUrl: 'https://x.y/#TICKET#'\n    sectionWidth: 260\n  themeVariables:\n    cScale2: \"#203040\"\n    background: \"#101010\"\n---\nkanban\n  todo[Todo]\n    a[Card]@{ ticket: T-1, label: 'Renamed', priority: 'Very High' }"),
        ("still being written", "kanban\n  todo[Todo]\n    [\"\"]\n    a[Card]@{ assigned: "),
        ("what nobody means to write", "kanban\n    todo[Todo]\n  a[Card]\n  b[Card]@{ colour: red }"),
        ("nothing to draw", "kanban"),
    ];

    private static Laid Build(string source, double room = 700) =>
        new KanbanBuilder(MermaidBuilders.Read(source), EditState.For(source), StyleFormat.Dark, isReadOnly: true).Lay(room);

    [TestMethod]
    public void ColumnsStandSideBySide_WithTheirCardsStackedInThem() => UiThread.Run(() =>
    {
        var laid = Build(Board);
        var columns = Pieces(laid, KanbanPiece.Column);
        var cards = Pieces(laid, KanbanPiece.Card);

        Assert.AreEqual(2, columns.Count);
        Assert.IsTrue(columns[1].Bounds.Left > columns[0].Bounds.Right - 1, "the second column right of the first");
        Assert.AreEqual(4, cards.Count);
        Assert.IsTrue(cards.Take(2).All(card => columns[0].Bounds.Contains(card.Bounds)) && cards.Skip(2).All(card => columns[1].Bounds.Contains(card.Bounds)));
        Assert.IsTrue(cards[1].Bounds.Top > cards[0].Bounds.Bottom, "each card under the one written before it");
        Assert.IsTrue(Written(Board, cards[2].Part).StartsWith("id4[", System.StringComparison.Ordinal));
    });

    [TestMethod]
    public void ALongTitleWrapsInsideItsCard_EachLineTheCharactersWritten() => UiThread.Run(() =>
    {
        var laid = Build(Board);
        var card = Pieces(laid, KanbanPiece.Card)[2];
        var lines = card.SelfAndDescendants().Where(piece => piece.Kind == KanbanPiece.Title).ToList();

        Assert.IsTrue(lines.Count > 1, "wrapped");
        Assert.IsTrue(lines.All(line => card.Bounds.Contains(line.Bounds)), "inside its card");
        Assert.AreEqual("Create parsing tests that cover every case anybody could think of writing", string.Concat(lines.Select(line => Written(Board, line.Part))));
    });

    [TestMethod]
    public void ATicketAndAnAssigneeSitUnderTheTitle_AndAPriorityIsAStripe() => UiThread.Run(() =>
    {
        var laid = Build(Board);
        var card = Pieces(laid, KanbanPiece.Card)[2];
        var ticket = Pieces(laid, KanbanPiece.Ticket).Single();
        var assigned = Pieces(laid, KanbanPiece.Assigned).First();

        Assert.AreEqual("MC-2038", ticket.Words!.Glyphs.Text);
        Assert.IsTrue(assigned.Bounds.Left > ticket.Bounds.Right, "the assignee right of the ticket");
        Assert.IsTrue(ticket.Bounds.Top >= card.SelfAndDescendants().Where(piece => piece.Kind == KanbanPiece.Title).Max(line => line.Bounds.Bottom) - 1, "under the title");
        Assert.AreEqual(2, Pieces(laid, KanbanPiece.Priority).Count, "High and Very Low");
    });
}
