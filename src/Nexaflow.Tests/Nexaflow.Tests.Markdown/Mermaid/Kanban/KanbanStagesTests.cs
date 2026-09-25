using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Kanban;

/// <summary>
/// What a <c>kanban</c> block's stage writes into its tree: which nodes are columns and which are the cards under them, what
/// each one's metadata says, and what the front matter asks for.
/// </summary>
[TestClass]
[CoversNode("kanban-ast")]
public class KanbanStagesTests
{
    [TestMethod]
    public void TheDocumentedBoardIsItsColumnsAndTheCardsUnderEach()
    {
        var nodes = MermaidStaged.Read(KanbanGrammarTests.Full).SelfAndDescendants().Where(node => node is KanbanColumnNode or KanbanCardNode).ToList();

        var under = new List<int>();
        foreach (var node in nodes)
        {
            if (node is KanbanColumnNode) under.Add(0);
            else under[^1]++;
        }

        CollectionAssert.AreEqual(new[] { 2, 1, 1, 2, 3, 1 }, under.ToArray(), "each card is written under the column it is in");
    }

    [TestMethod]
    public void ACardsMetadataIsReadWithoutItsQuotes_AndItsTicketLinked()
    {
        var card = Cards(MermaidStaged.Read(KanbanGrammarTests.Full)).Single(card => card.Ticket == "MC-2038");

        Assert.AreEqual(("K.Sveidqvist", "High"), (card.Assigned, card.Priority));
        Assert.IsTrue(card.Linked, "the front matter says where tickets are");

        var labelled = Cards(MermaidStaged.Read("kanban\n  todo[Todo]\n    a[Card]@{ assigned: 'Smith, J', label: 'Renamed', ticket: 7 }")).Single();
        Assert.AreEqual(("Smith, J", "Renamed"), (labelled.Assigned, labelled.Label));
        Assert.IsFalse(labelled.Linked, "and without it a ticket links nowhere");
    }

    [TestMethod]
    public void AColumnsLabelIsItsMetadatas()
    {
        var column = MermaidStaged.Read("kanban\n  todo[Todo]@{ label: \"Doing\" }\n    a[Card]").SelfAndDescendants().OfType<KanbanColumnNode>().Single();
        Assert.AreEqual("Doing", column.Label);
    }

    [TestMethod]
    public void ANodeWrittenLeftOfTheFirstColumnIsNeither_AndSaysWhy()
    {
        var tree = MermaidStaged.Read("kanban\n    todo[Todo]\n      a[Card]\n  b[Stray]");

        Assert.AreEqual(1, tree.SelfAndDescendants().OfType<KanbanColumnNode>().Count());
        Assert.AreEqual(1, Cards(tree).Count);
        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == KanbanKinds.Node && node.Trouble is not null));
    }

    [TestMethod]
    public void WhatTheStageWritesIsNoPartOfTheSource()
    {
        foreach (var source in new[] { KanbanGrammarTests.Full, "kanban\n  todo[Todo]\n    [\"\"]", "kanban\n    todo\n  b" })
        {
            Assert.AreEqual(source, MermaidStaged.Read(source).Print(), "a stage leaves the characters alone");
            Assert.AreEqual(source, MermaidStaged.Read(source, holes: true).Print());
        }
    }

    [TestMethod]
    public void TheFrontMatterIsRead()
    {
        var config = ((ConfiguredNode<KanbanConfig>)MermaidStaged.Read("---\nconfig:\n  kanban:\n    sectionWidth: 250\n  themeVariables:\n    cScale2: \"#ff0000\"\n    background: \"#101010\"\n---\nkanban\n  Todo")).Config;

        Assert.AreEqual((250d, "#ff0000", "#101010"), (config.SectionWidth!.Value, config.Scale[2], config.Background));
    }

    private static List<KanbanCardNode> Cards(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<KanbanCardNode>()];
}
