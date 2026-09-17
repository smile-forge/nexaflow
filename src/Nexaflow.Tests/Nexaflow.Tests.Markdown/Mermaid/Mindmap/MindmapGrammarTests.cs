using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Mindmap;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Mindmap;

/// <summary>
/// What a <c>mindmap</c> block is read into: nodes with their ids and titles in any of Mermaid's brackets, and the icon and class
/// lines decorating them — and what is wrong with a node hanging off nothing.
/// </summary>
[TestClass]
[CoversNode("mindmap-ast")]
public class MindmapGrammarTests : MermaidGrammarContract
{
    /// <summary>The mindmap the Mermaid documentation opens with.</summary>
    public const string Example =
        """
        mindmap
          root((mindmap))
            Origins
              Long history
              ::icon(fa fa-book)
              Popularisation
                British popular psychology author Tony Buzan
            Research
              On effectiveness<br/>and features
              On Automatic creation
                Uses
                    Creative techniques
                    Strategic planning
                    Argument mapping
            Tools
              Pen and paper
              Mermaid
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Mindmap;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Example,
        "mindmap\n    Root\n        A\n            B\n            C",
        "mindmap\n    id[I am a square]",
        "mindmap\n    id(I am a rounded square)",
        "mindmap\n    id((I am a circle))",
        "mindmap\n    id))I am a bang((",
        "mindmap\n    id)I am a cloud(",
        "mindmap\n    id{{I am a hexagon}}",
        "mindmap\n    I am the default shape",
        "mindmap\n    Root\n        A\n        ::icon(fa fa-book)\n        B(B)\n        ::icon(mdi mdi-skull-outline)",
        "mindmap\n    Root\n        A[A]\n        :::urgent large\n        B(B)\n        C",
        "mindmap\n    Root\n        A\n            B\n          C",
        "mindmap\n    id1[\"`**Root** with a second line`\"]\n      id2[\"`The dog in **the** hog`\"]\n      id3[Regular labels still works]",
        "---\nconfig:\n  layout: tidy-tree\n---\nmindmap\nroot((mindmap is a long thing))\n  A\n  B\n  C\n  D",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a comment and a blank line", "mindmap\n  root((r)) %% the middle\n\n  %% branches\n    A"),
        ("written on Windows", "mindmap\r\n  root((r))  \r\n    A\r\n"),
        ("a title in quotes", "mindmap\n  root[\"In &quot;quotes&quot;\"]\n    A"),
        // Half written.
        ("nothing but the keyword", "mindmap"),
        ("only the root", "mindmap\n  root((r))"),
        ("a title still to write", "mindmap\n  root((r))\n    [\"\"]"),
        // What nobody means to write.
        ("a title never closed", "mindmap\n  root((r))\n    A[Square"),
        ("a second root", "mindmap\n  root((r))\n    A\n  another"),
        ("something after the title", "mindmap\n  root((r)) extra"),
    ];

    [TestMethod]
    public void TheDocumentedMindmapsNodesAndDecorationsAreEachRead()
    {
        var tree = MermaidParser.Read(Example);

        Assert.AreEqual(15, Nodes(tree, MindmapKinds.Node).Count);
        Assert.AreEqual(1, Nodes(tree, MindmapKinds.Icon).Count);
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("mindmap\n  root((r))\n    A\n  another", "one root"),
                     ("mindmap\n  root((r))\n    A[Square", "never closed"),
                     ("mindmap\n  root((r)) extra", "A node is"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ANewLineUnderANodeIsAnotherAsFarInAsIt()
    {
        var grammar = new MindmapGrammar();
        var node = Nodes(MermaidParser.Read("mindmap\n  root((r))\n    A"), MindmapKinds.Node)[1];

        Assert.AreEqual(("[\"\"]", 2), grammar.Blank(node));
        Assert.IsNull(grammar.Blank(above: null));
    }

    [TestMethod]
    public void ABracketTypedIntoABareIdWritesItAsATitleInQuotes()
    {
        const string source = "mindmap\n  root((r))\n    Origins";
        var id = ContentReading.Of(MermaidParser.Read(source)).Root.SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Text == "Origins");
        var writing = new MindmapGrammar().Escaping(id, id.End, "(")!.Value;

        Assert.AreEqual("mindmap\n  root((r))\n    [\"Origins(\"]", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidParser.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
