using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Kanban;

/// <summary>A <c>kanban</c> block read back into its columns, the cards under each, their metadata, and its front matter.</summary>
[TestClass]
[CoversNode("kanban-ast")]
public class KanbanBoardTests
{
    [TestMethod]
    public void TheDocumentedBoardIsItsColumnsAndTheCardsUnderEach()
    {
        var board = KanbanBoard.Of(MermaidStaged.Read(KanbanGrammarTests.Full));

        CollectionAssert.AreEqual(new[] { "Todo", "In progress", "Ready for deploy", "Ready for test", "Done", "Can't reproduce" },
                                  board.Columns.Select(column => column.Title!.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 1, 1, 2, 3, 1 }, board.Columns.Select(column => column.Cards.Count).ToArray());
        Assert.AreEqual("Create Documentation", board.Columns[0].Cards[0].Title!.Text);
    }

    [TestMethod]
    public void ACardsMetadataIsReadWithoutItsQuotes_AndItsTicketLinked()
    {
        var card = KanbanBoard.Of(MermaidStaged.Read(KanbanGrammarTests.Full)).Columns[3].Cards[0];

        Assert.AreEqual(("MC-2038", "K.Sveidqvist", "High"), (card.Ticket, card.Assigned, card.Priority));
        Assert.AreEqual("https://mermaidchart.atlassian.net/browse/MC-2038", card.Link);

        var labelled = KanbanBoard.Of(MermaidStaged.Read("kanban\n  todo[Todo]\n    a[Card]@{ assigned: 'Smith, J', label: 'Renamed' }")).Columns[0].Cards[0];
        Assert.AreEqual(("Smith, J", "Renamed"), (labelled.Assigned, labelled.Label));
    }

    [TestMethod]
    public void TheFrontMatterIsRead()
    {
        var config = KanbanBoard.Of(MermaidStaged.Read("---\nconfig:\n  kanban:\n    sectionWidth: 250\n  themeVariables:\n    cScale2: \"#ff0000\"\n    background: \"#101010\"\n---\nkanban\n  Todo")).Config;

        Assert.AreEqual((250d, "#ff0000", "#101010"), (config.SectionWidth!.Value, config.Scale[2], config.Background));
    }
}
