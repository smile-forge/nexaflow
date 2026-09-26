using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
        Laying.Lay("mermaid", source, room);

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
    public void ABranchSweepsFromItsParentsSideIntoTheMiddleOfItsChildsNearSide() => UiThread.Run(() =>
    {
        var laid = Build(Map);
        var (parent, child) = (Node(laid, Map, "Markdown").Bounds, Node(laid, Map, "CommonMark").Bounds);
        var branch = Pieces(laid, MindmapPiece.Branch).Single(one => Written(Map, one.Part).Trim() == "CommonMark").Bounds;

        Assert.AreEqual(parent.Left, branch.Right, 2, "out of the parent's side facing the child");
        Assert.AreEqual(child.Right, branch.Left, 2, "into the child's near side");
        Assert.AreEqual(parent.Top + (parent.Height / 2), branch.Bottom, 2, "from the middle of the parent's side");
        Assert.AreEqual(child.Top + (child.Height / 2), branch.Top, 2, "to the middle of the child's");
    });

    [TestMethod]
    public void ANodeIsWashedAndEdgedInItsBranchsColour_ItsWordsInTheDiagramsInk() => UiThread.Run(() =>
    {
        var laid = Build(Map);
        var node = Node(laid, Map, "CommonMark");
        var shape = node.SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<GeometryMark>().First(mark => mark.Fill is not null);
        var line = Pieces(laid, MindmapPiece.Branch).Single(one => Written(Map, one.Part).Trim() == "CommonMark")
            .SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<GeometryMark>().First(mark => mark.Stroke is not null);
        var words = node.SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<TextMark>().First();

        Assert.AreEqual(((SolidColorBrush)line.Stroke!).Color, ((SolidColorBrush)shape.Stroke!).Color, "edged in its branch's colour");
        Assert.AreEqual(((SolidColorBrush)shape.Stroke).Color, ((SolidColorBrush)shape.Fill!).Color, "washed in it");
        Assert.IsTrue(shape.Fill.Opacity < 0.5, "and only washed, not filled solid");
        Assert.AreSame(StyleFormat.Dark.Text, words.Foreground, "its words in the diagram's own ink");
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

    /// <summary>What a branch is drawn in, by the node it reaches.</summary>
    private static Color Stroke(Laid laid, string source, string reaches) =>
        ((SolidColorBrush)Pieces(laid, MindmapPiece.Branch).Single(one => Written(source, one.Part).Trim() == reaches)
            .SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<GeometryMark>().First(mark => mark.Stroke is not null).Stroke!).Color;

    [TestMethod]
    public void UnclearIndentationHangsANodeOffTheNearestNodeIndentedLess() => UiThread.Run(() =>
    {
        const string source = "mindmap\n    Root\n        A\n            B\n          C";
        var laid = Build(source);
        var parent = Node(laid, source, "A").Bounds;
        var branch = Pieces(laid, MindmapPiece.Branch).Single(one => Written(source, one.Part).Trim() == "C").Bounds;

        Assert.AreEqual(parent.Left, branch.Right, 2, "C is neither B's child nor its sibling by indentation, so it is A's child, as Mermaid reads it");
    });

    [TestMethod]
    public void EachBranchOffTheRootIsItsOwn_AndEveryNodeUnderItTakesIt() => UiThread.Run(() =>
    {
        const string source = "mindmap\nr\n a\n b\n  c\n d\n  e\n   f\n g";
        var laid = Build(source);

        Assert.AreEqual(4, new[] { "a", "b", "d", "g" }.Select(child => Stroke(laid, source, child)).Distinct().Count(), "every child of the root a colour of its own");
        Assert.AreEqual(Stroke(laid, source, "b"), Stroke(laid, source, "c"));
        Assert.IsTrue(new[] { "e", "f" }.All(under => Stroke(laid, source, under) == Stroke(laid, source, "d")), "and everything under it that colour");
    });
}
