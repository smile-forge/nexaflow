using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Markdown.Mermaid.Kanban.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Kanban;

/// <summary>
/// What <see cref="KanbanParser"/> reads a <c>kanban</c> block into: nodes with their ids, titles in any of Mermaid's brackets and metadata, icon and class
/// lines — and which nodes are columns, by their indentation. The header is optional, since a <c>kanban</c> fence already says what
/// the block is.
/// </summary>
[TestClass]
[CoversNode("kanban-ast")]
public class KanbanParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => KanbanParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Full,
        "kanban\n  todo[A long\n  title]\n    a[Card]@{ unread: key } %% a note",
        "kanban\n  todo[Todo]\n    a[Card]@{\n      assigned: knsv\n      priority: 'High'\n    }",
        "kanban\n  todo[The \"next\" thing]",
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
        ("a title in quotes across a line ending", "kanban\n  todo[\"To\ndo\"]\n    a[Card]"),
        ("metadata across several lines", "kanban\n  todo[Todo]\n    a[Card]@{\n      assigned: knsv,\n      priority: 'High'\n    }\n    b[Other]"),
        ("a title in quotes still being written, a card under it", "kanban\n  todo[Todo]\n    a[\"Car\n    b[\"Other\"]"),
        ("metadata still being written, a card under it", "kanban\n  todo[Todo]\n    a[Card]@{ assigned: \n    b[Other]@{ priority: 'Low' }"),
        ("no header, as a kanban fence holds it", "todo[Todo]\n  a[Card]"),
        ("a binding supplying cards", "kanban\n  todo[Todo]\n    {{Cards}}\n"),
    ];

    /// <summary>
    /// The stages a kanban board's language offers the engine (<c>Shipped.Kanban</c>), named again here — what the engine adds for
    /// every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes)
    {
        var config = KanbanConfig.Read(MermaidBlock.Of(tree).Config);

        return
        [
            new ResolveColumns(config),
            new WithConfig<KanbanConfig>(config),
            new WithIcons(),
            .. holes ? [new WithHoles(KanbanParser.Holds)] : Array.Empty<IAstStage>(),
        ];
    }

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = KanbanParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceAKanbanFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("todo[Todo]\n  a[Card]");

        Assert.AreEqual(KanbanParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, Nodes(tree, KanbanKinds.Node).Count, "the first line is a column, not a header gone wrong");
        Assert.AreEqual(0, Trouble("todo[Todo]\n  a[Card]").Count);
    }

    [TestMethod]
    public void ATitleInQuotesAndMetadataRunToWhatClosesThem_AcrossLineEndings()
    {
        const string titled = "kanban\n  todo[\"To\ndo\"]\n    a[Card]";
        const string data = "kanban\n  todo[Todo]\n    a[Card]@{\n      assigned: knsv,\n      priority: 'High'\n    }\n    b[Other]";

        Assert.AreEqual(2, Nodes(Staged(titled), KanbanKinds.Node).Count);
        Assert.AreEqual(0, Trouble(titled).Count, string.Join(" | ", Trouble(titled)));
        Assert.AreEqual(3, Nodes(Staged(data), KanbanKinds.Node).Count);
        Assert.AreEqual(0, Trouble(data).Count, string.Join(" | ", Trouble(data)));
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenTakesNoLineUnderIt()
    {
        // A quote or metadata still being written ends with its line, so the card under it reads as the card it is.
        foreach (var source in new[] { "kanban\n  todo[Todo]\n    a[\"Car\n    b[\"Other\"]", "kanban\n  todo[Todo]\n    a[Card]@{ assigned: \n    b[Other]@{ priority: 'Low' }" })
            Assert.AreEqual(4, KanbanParser.Parse(source).Children.Count, $"{source}: the header and three lines of their own");
    }

    [TestMethod]
    public void TheDocumentedBoardsNodesAreEachRead_AndItsColumnsAreThoseAsFarInAsTheFirst()
    {
        var tree = Staged(Full);

        Assert.AreEqual(16, Nodes(tree, KanbanKinds.Node).Count);
        Assert.AreEqual(6, Nodes(tree, KanbanKinds.Node).Count(node => node is KanbanColumnNode));
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
                     
                     ("kanban\n  todo[Todo]\n    a[Card]@{ assigned: knsv", "closed with }"),
                     ("kanban\n  todo[Todo]\n    a[Card]@{ knsv }", "key: value"),
                     ("kanban\n  todo[Todo", "never closed"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}

