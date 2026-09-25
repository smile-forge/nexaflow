using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Kanban;

/// <summary>
/// What a <c>kanban</c> block is read into: nodes with their ids, titles in any of Mermaid's brackets and metadata, icon and class
/// lines — and which nodes are columns, by their indentation.
/// </summary>
[TestClass]
[CoversNode("kanban-ast")]
public class KanbanGrammarTests : MermaidGrammarContract
{
    /// <summary>The documentation's full example.</summary>
    public const string Full =
        """
        ---
        config:
          kanban:
            ticketBaseUrl: 'https://mermaidchart.atlassian.net/browse/#TICKET#'
        ---
        kanban
          Todo
            [Create Documentation]
            docs[Create Blog about the new diagram]
          [In progress]
            id6[Create renderer so that it works in all cases. We also add some extra text here for testing purposes. And some more just for the extra flare.]
          id9[Ready for deploy]
            id8[Design grammar]@{ assigned: 'knsv' }
          id10[Ready for test]
            id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
            id66[last item]@{ priority: 'Very Low', assigned: 'knsv' }
          id11[Done]
            id5[define getData]
            id2[Title of diagram is more than 100 chars when user duplicates diagram with 100 char]@{ ticket: MC-2036, priority: 'Very High'}
            id3[Update DB function]@{ ticket: MC-2037, assigned: knsv, priority: 'High' }

          id12[Can't reproduce]
            id3[Weird flickering in Firefox]
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Kanban;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Full,
        "kanban\n  column1[Column Title]\n    task1[Task Description]",
        "kanban\ntodo[Todo]\n  id3[Update Database Function]@{ ticket: MC-2037, assigned: 'knsv', priority: 'High' }",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("every bracket", "kanban\n  a(Round)\n    b((Circle))\n    c{{Hexagon}}\n    d)Cloud(\n    e))Bang((\n    f[\"In quotes\"]"),
        ("icons and classes", "kanban\n  todo[Todo]\n    a[Card]\n    ::icon(fa fa-book)\n    :::urgent"),
        ("a comment and a blank line", "kanban\n  todo[Todo] %% first\n\n  %% cards\n    a[Card]"),
        ("written on Windows", "kanban\r\n  todo[Todo]  \r\n    a[Card]\r\n"),
        ("metadata with a comma in quotes and a label", "kanban\n  todo[Todo]\n    a[Card]@{ assigned: 'Smith, J', label: 'Renamed' }"),
        // Half written.
        ("nothing but the keyword", "kanban"),
        ("a title still to write", "kanban\n  todo[Todo]\n    [\"\"]"),
        ("metadata still being written", "kanban\n  todo[Todo]\n    a[Card]@{ assigned: "),
        ("metadata never closed", "kanban\n  todo[Todo]\n    a[Card]@{ assigned: knsv"),
        ("empty metadata", "kanban\n  todo[Todo]\n    a[Card]@{}"),
        // What nobody means to write.
        ("a title never closed", "kanban\n  todo[Todo"),
        ("a card left of its column", "kanban\n    todo[Todo]\n  a[Card]"),
        ("metadata nobody knows", "kanban\n  todo[Todo]\n    a[Card]@{ colour: red }"),
        ("metadata that is no key and value", "kanban\n  todo[Todo]\n    a[Card]@{ knsv }"),
        ("something after the title", "kanban\n  todo[Todo] extra"),
    ];

    [TestMethod]
    public void TheDocumentedBoardsNodesAreEachRead_AndItsColumnsAreThoseAsFarInAsTheFirst()
    {
        var tree = MermaidStaged.Read(Full);

        Assert.AreEqual(16, Nodes(tree, KanbanKinds.Node).Count);
        Assert.AreEqual(6, Nodes(tree, KanbanKinds.Node).Count(node => node.Said(KanbanRoles.Column) is not null));
        Assert.AreEqual(5, Nodes(tree, KanbanKinds.Data).Count);
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "kanban\n  todo[Todo]\n    [\"\"]", "kanban\n  todo[Todo]\n    a[Card]@{}" })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("kanban\n    todo[Todo]\n  a[Card]", "left of the first column"),
                     ("kanban\n  todo[Todo]\n    a[Card]@{ colour: red }", "Metadata sets"),
                     ("kanban\n  todo[Todo]\n    a[Card]@{ assigned: knsv", "closed with }"),
                     ("kanban\n  todo[Todo]\n    a[Card]@{ knsv }", "key: value"),
                     ("kanban\n  todo[Todo", "never closed"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ANewLineUnderAColumnIsACardIndentedUnderIt_AndUnderACardAnother()
    {
        var grammar = new KanbanGrammar();
        var nodes = Nodes(MermaidStaged.Read("kanban\n  todo[Todo]\n    a[Card]"), KanbanKinds.Node);

        Assert.AreEqual(("  [\"\"]", 4), grammar.Blank(nodes[0]));
        Assert.AreEqual(("[\"\"]", 2), grammar.Blank(nodes[1]));
    }

    [TestMethod]
    public void ABracketTypedIntoABareIdWritesItAsATitleInQuotes()
    {
        const string source = "kanban\n  Todo";
        var id = ContentReading.Of(MermaidStaged.Read(source)).Root.SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Text == "Todo");
        var writing = new KanbanGrammar().Escaping(id, id.End, "(")!.Value;

        Assert.AreEqual("kanban\n  [\"Todo(\"]", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
