using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Mindmap;
using Nexaflow.Markdown.Mermaid.Mindmap.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Mindmap;

/// <summary>
/// What <see cref="MindmapParser"/> reads a <c>mindmap</c> block into: nodes with their ids and titles in any of Mermaid's brackets, and the icon and class
/// lines decorating them — and what is wrong with a node hanging off nothing. The header is optional, since a <c>mindmap</c> fence
/// already says what the block is.
/// </summary>
[TestClass]
[CoversNode("mindmap-ast")]
public class MindmapParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => MindmapParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Example,
        "mindmap\n  root[A long\n  title]\n    child[50%% done]\n    bare %% and all",
        "mindmap\n  root[The \"best\" idea]\n    child(\"quoted\" then not)",
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
        ("a title in quotes across a line ending", "mindmap\n  root((\"Two\nlines\"))\n    A"),
        ("a markdown title across a line ending", "mindmap\n  root[\"**Root**\nwith a second line\"]\n    A"),
        ("a title in quotes still being written, a node under it", "mindmap\n  root((r))\n    A[\"Squ\n    B[\"Other\"]"),
        ("no header, as a mindmap fence holds it", "root((r))\n  A\n  B"),
        ("a binding supplying nodes", "mindmap\n  root((r))\n    {{Branches}}\n"),
    ];

    /// <summary>
    /// The stages a mindmap's language offers the engine (<c>Shipped.Mindmap</c>), named again here — what the engine adds for every
    /// language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolveRoot(),
        new WithConfig<MindmapConfig>(MindmapConfig.Read(MermaidBlock.Of(tree).Config)),
        new WithIcons(),
        .. holes ? [new WithHoles(MindmapParser.Holds)] : Array.Empty<IAstStage>(),
    ];

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = MindmapParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceAMindmapFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("root((r))\n  A\n  B");

        Assert.AreEqual(MindmapParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(3, Nodes(tree, MindmapKinds.Node).Count, "the first line is the root, not a header gone wrong");
        Assert.AreEqual(0, Trouble("root((r))\n  A\n  B").Count);
    }

    [TestMethod]
    public void ATitleInQuotesRunsToTheQuoteClosingIt_AndTheNodeIsOneLineOfTheTree()
    {
        const string source = "mindmap\n  root((\"Two\nlines\"))\n    A";

        Assert.AreEqual(3, MindmapParser.Parse(source).Children.Count, "the header, the root across two rows, and the node under it");
        Assert.AreEqual(2, Nodes(Staged(source), MindmapKinds.Node).Count);
        Assert.AreEqual(0, Trouble(source).Count, string.Join(" | ", Trouble(source)));
    }

    [TestMethod]
    public void ATitleInQuotesStillBeingWrittenTakesNoLineUnderIt() =>
        Assert.AreEqual(4, MindmapParser.Parse("mindmap\n  root((r))\n    A[\"Squ\n    B[\"Other\"]").Children.Count, "the header and three lines of their own");

    [TestMethod]
    public void TheDocumentedMindmapsNodesAndDecorationsAreEachRead()
    {
        var tree = Staged(Example);

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

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}

