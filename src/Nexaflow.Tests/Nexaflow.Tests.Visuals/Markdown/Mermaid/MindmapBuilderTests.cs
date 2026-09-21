using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Mindmap;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>mindmap</c> block drawn on the shared layout tree: a node standing for each line, branches standing for the nodes they
/// reach, the root's children either side of it, and every title typed into where it is drawn.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mindmap")]
public class MindmapBuilderTests : MermaidBuilderContract
{
    private const string Map =
        "mindmap\n  root((Nexaflow))\n    Markdown\n      CommonMark\n      Mermaid\n    Files\n      Explorer\n    Terminal";

    public override MermaidDiagram Diagram => MermaidDiagram.Mindmap;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("a mindmap with branches", Map),
        ("every shape", "mindmap\n  r((root))\n    a[Square]\n    b(Rounded)\n    c((Circle))\n    d)Cloud(\n    e))Bang((\n    f{{Hexagon}}\n    g Plain"),
        ("a line break and a long title that wraps", "mindmap\n  root((r))\n    On effectiveness<br/>and features\n    On automatic creation of mindmaps from an outline of text that runs on"),
        ("icons, classes and a theme of its own",
            "---\nconfig:\n  mindmap:\n    padding: 14\n    maxNodeWidth: 140\n  themeVariables:\n    cScale1: \"#203040\"\n    git0: \"#402030\"\n---\nmindmap\n  root((r))\n    A\n    ::icon(fa fa-book)\n    :::urgent"),
        ("unclear indentation", "mindmap\n    Root\n        A\n            B\n          C"),
        ("still being written", "mindmap\n  root((r))\n    [\"\"]"),
        ("what nobody means to write", "mindmap\n  root((r))\n    A\n  another"),
        ("nothing to draw", "mindmap"),
    ];

    private static Laid Build(string source, double room = 700) =>
        MindmapBuilder.Build(MermaidBuilders.Read(source), new DiagramLaying(MarkdownPalette.Dark, room));

    private static Piece Node(Laid laid, string source, string title) =>
        Pieces(laid, MindmapPiece.Node).Single(node => Written(source, node.Part).Contains(title, System.StringComparison.Ordinal));

    [TestMethod]
    public void EveryNodeStandsForItsLine_AndEveryBranchForTheNodeItReaches() => UiThread.Run(() =>
    {
        var laid = Build(Map);

        Assert.AreEqual(7, Pieces(laid, MindmapPiece.Node).Count);
        CollectionAssert.AreEquivalent(new[] { "Markdown", "CommonMark", "Mermaid", "Files", "Explorer", "Terminal" },
                                       Pieces(laid, MindmapPiece.Branch).Select(branch => Written(Map, branch.Part).Trim()).ToArray());
    });

    [TestMethod]
    public void TheRootsChildrenTakeTurnsEitherSideOfIt_AndAChildIsBesideItsParent() => UiThread.Run(() =>
    {
        var laid = Build(Map);
        var root = Node(laid, Map, "root((Nexaflow))");

        Assert.IsTrue(Node(laid, Map, "Markdown").Bounds.Right <= root.Bounds.Left + 1, "the first branch left of the root");
        Assert.IsTrue(Node(laid, Map, "Files").Bounds.Left >= root.Bounds.Right - 1, "the second right of it");
        Assert.IsTrue(Node(laid, Map, "CommonMark").Bounds.Right <= Node(laid, Map, "Markdown").Bounds.Left + 1, "and a child further out on its parent's side");
    });

    [TestMethod]
    public void ATitleIsTheCharactersWritten_WrappedAndBrokenWhereABreakSaysSo() => UiThread.Run(() =>
    {
        const string source = "mindmap\n  root((r))\n    On effectiveness<br/>and features";
        var laid = Build(source);
        var lines = Pieces(laid, MindmapPiece.Title).Where(title => title.Part!.Start > source.IndexOf("On ", System.StringComparison.Ordinal) - 1).ToList();

        Assert.IsTrue(lines.All(line => line.Words is { Maps: true }), "every line typed into where it is drawn");
        CollectionAssert.AreEqual(new[] { "On effectiveness", "and features" }, lines.Select(line => Written(source, line.Part)).ToArray(),
                                  "the break is not drawn, and each line stands for its own characters");
    });

    [TestMethod]
    public void APressOnABranchMeansTheNodeItReaches() => UiThread.Run(() =>
    {
        var laid = Build(Map);
        var branch = Pieces(laid, MindmapPiece.Branch).Single(one => Written(Map, one.Part).Trim() == "CommonMark");
        var middle = new Point(branch.Bounds.X + (branch.Bounds.Width / 2), branch.Bounds.Y + (branch.Bounds.Height / 2));

        Assert.AreEqual("CommonMark", Written(Map, laid.Root.PieceAt(middle).Part).Trim(), "pressing the branch means the node it reaches");
    });
}
