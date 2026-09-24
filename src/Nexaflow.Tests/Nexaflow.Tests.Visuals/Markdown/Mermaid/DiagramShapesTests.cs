using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Editing;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The shapes a diagram draws its nodes as: each holds the words it is made to the size of, a connector meets it at its
/// outline, and it stands in that outline on the layout tree.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramShapesTests
{
    private static readonly DiagramShape[] Shapes = Enum.GetValues<DiagramShape>();

    [TestMethod]
    public void EveryShapeHoldsTheWordsItIsMadeToTheSizeOf() => UiThread.Run(() =>
    {
        foreach (var shape in Shapes)
            foreach (var words in new[] { new Size(60, 14), new Size(12, 30), new Size(220, 16) })
            {
                var size = DiagramShapes.Around(shape, words, pad: 6);
                var bounds = new Rect(new Point(10, 20), size);
                var inside = DiagramShapes.Inside(shape, bounds);

                Assert.IsTrue(inside.Width >= words.Width - 1e-6 && inside.Height >= words.Height - 1e-6,
                              $"{shape}: {words} fits in {inside}");

                var outline = DiagramShapes.Outline(shape, bounds);
                var room = new Rect(inside.X + (inside.Width / 2) - (words.Width / 2) + 0.5, inside.Y + (inside.Height / 2) - (words.Height / 2) + 0.5,
                                    Math.Max(0, words.Width - 1), Math.Max(0, words.Height - 1));

                foreach (var corner in new[] { room.TopLeft, room.TopRight, room.BottomLeft, room.BottomRight })
                    Assert.IsTrue(outline.FillContains(corner), $"{shape}: the words' corner {corner} is inside its outline");
            }
    });

    [TestMethod]
    public void AShapesOutlineFillsItsBounds() => UiThread.Run(() =>
    {
        var bounds = new Rect(5, 5, 120, 60);

        foreach (var shape in Shapes)
        {
            var outline = DiagramShapes.Outline(shape, bounds);
            Assert.IsTrue(outline.IsFrozen, $"{shape} is frozen, so it can be drawn from any thread");
            Assert.AreEqual(bounds.Width, outline.Bounds.Width, 1, $"{shape}: as wide as its bounds");
            Assert.AreEqual(bounds.Height, outline.Bounds.Height, 1.5, $"{shape}: as tall as its bounds");
        }
    });

    [TestMethod]
    public void AConnectorMeetsAShapeAtItsOutline() => UiThread.Run(() =>
    {
        var bounds = new Rect(0, 0, 120, 60);
        var edge = new Pen(Brushes.Black, 2);

        foreach (var shape in new[]
                 {
                     DiagramShape.Rectangle, DiagramShape.Circle, DiagramShape.Diamond, DiagramShape.Hexagon, DiagramShape.Asymmetric,
                     DiagramShape.Parallelogram, DiagramShape.ParallelogramAlt, DiagramShape.Trapezoid, DiagramShape.TrapezoidAlt, DiagramShape.Card,
                 })
        {
            var outline = DiagramShapes.Outline(shape, bounds);

            for (var turn = 0; turn < 16; turn++)
            {
                var angle = turn * Math.PI / 8;
                var toward = new Point(60 + (500 * Math.Cos(angle)), 30 + (500 * Math.Sin(angle)));
                var met = DiagramShapes.Edge(shape, bounds, toward);

                Assert.IsTrue(outline.StrokeContains(edge, met), $"{shape}: a line toward {toward.ToString(CultureInfo.InvariantCulture)} meets it at {met}, on its outline");
            }
        }
    });

    [TestMethod]
    public void AShapeStandsInItsOutline_WithItsWordsInTheMiddle() => UiThread.Run(() =>
    {
        var words = Words("Decide", new TestPart(0, 6));
        var bounds = new Rect(new Point(40, 30), DiagramShapes.Around(DiagramShape.Diamond, new Size(words.Width, words.Height), pad: 6));

        var build = new LayoutBuilder();
        build.Open("page");

        // Something under it, so a press that misses the diamond has something else to mean.
        build.Open("Page", new TestPart(20, 1), stops: Stops.None);
        build.Draw(new RuleMark(new Rect(0, 0, 400, 300), Brushes.White));
        build.Close();

        DiagramShapes.Draw(build, "Node", new TestPart(0, 6), DiagramShape.Diamond, bounds, Brushes.White, new DiagramStroke(Brushes.Black), words);
        build.Close();

        var root = build.Seal().Root;
        var node = root.SelfAndDescendants().Single(piece => piece.Kind == "Node");
        var said = root.SelfAndDescendants().Single(piece => piece.Kind == MermaidPiece.Words);

        var inside = root.PieceAt(new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 5)));
        Assert.IsTrue(inside.Kind == MermaidPiece.Shape && inside.Parent == node, $"a press inside the diamond means it, not the {inside.Kind}");
        Assert.AreEqual("Page", root.PieceAt(new Point(bounds.X + 2, bounds.Y + 2)).Kind, "and one in the corner of its bounds, outside it, means what is behind");
        Assert.AreEqual(bounds.X + (bounds.Width / 2), said.Bounds.X + (said.Bounds.Width / 2), 1, "the words are in the middle across");
        Assert.AreEqual(MermaidPiece.Words, root.PieceAt(new Point(said.Bounds.Right - 1, said.Bounds.Y + (said.Bounds.Height / 2))).Kind, "and a press on the words means them, not the shape under them");
    });

    /// <summary>Words worked out, which stand for nothing.</summary>
    internal static DiagramWords Words(string text, ISourcePart? part = null)
    {
        var set = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Black, 1.0);
        var letter = new FormattedText("x", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Black, 1.0);
        return new DiagramWords(set, part, hole: null, letter, Brushes.Black, maps: part is not null, writes: part is not null);
    }

    [TestMethod]
    public void AShapeWithWordsPlacedInItStandsLessThoseWordsAndWhatIsDrawnOverIt() => UiThread.Run(() =>
    {
        var bounds = new Rect(0, 0, 200, 120);
        var title = Words("Title", new TestPart(0, 5));
        var covered = new RectangleGeometry(new Rect(20, 60, 160, 40));

        var build = new LayoutBuilder();
        build.Open("page");
        DiagramShapes.Draw(build, "Column", new TestPart(10, 3), DiagramShape.Rectangle, bounds, Brushes.White, new DiagramStroke(Brushes.Black),
                           [(title, new Point(10, 10), "Title")], covered);

        // A card drawn after the column, where the column was told something is drawn over it: a press there is the card's,
        // though the column came first.
        build.Open("Card", new TestPart(20, 2), stops: Stops.None);
        build.Draw(new GeometryMark(covered, Brushes.Gray, null, 0));
        build.Occupies(covered);
        build.Close();
        build.Close();

        var root = build.Seal().Root;

        Assert.AreEqual("Title", root.PieceAt(new Point(12, 10 + (title.Height / 2))).Kind, "a press on the words means them");
        Assert.AreEqual(MermaidPiece.Shape, root.PieceAt(new Point(100, 45)).Kind, "one elsewhere in the shape means the shape");
        Assert.AreEqual("Card", root.PieceAt(new Point(100, 80)).Kind, "and one where something else is drawn over it means that");
    });

    [TestMethod]
    public void AShapesNameCanBeSetInABandOfItsOwnAcrossTheTopOfIt() => UiThread.Run(() =>
    {
        var bounds = new Rect(0, 0, 200, 120);
        var title = Words("Title", new TestPart(0, 5));

        var build = new LayoutBuilder();
        build.Open("page");
        DiagramShapes.Draw(build, "Group", new TestPart(10, 3), DiagramShape.Rounded, bounds, Brushes.White, new DiagramStroke(Brushes.Black),
                           [(title, new Point(10, 6), "Title")], band: Brushes.Navy);
        build.Close();

        var root = build.Seal().Root;
        var shape = root.SelfAndDescendants().Single(piece => piece.Kind == MermaidPiece.Shape);
        var marks = shape.Marks.ToArray().OfType<GeometryMark>().ToList();
        var band = marks.Single(mark => mark.Fill == Brushes.Navy).Shape.Bounds;

        Assert.AreEqual(bounds.Top, band.Top, 0.5, "the band runs from the top of the shape");
        Assert.AreEqual(bounds.Width, band.Width, 0.5, "and right across it");
        Assert.AreEqual(6 + title.Height + 6, band.Height, 0.5, "down past the words by as much air again as is over them");
        Assert.IsTrue(marks.Any(mark => mark.Shape is LineGeometry line && Math.Abs(line.StartPoint.Y - band.Bottom) < 0.5 && mark.Stroke == Brushes.Black),
                      "with a rule under it in the outline's ink");
        Assert.AreEqual(Brushes.White, marks[0].Fill, "over the shape's own fill");
        Assert.AreEqual(MermaidPiece.Shape, root.PieceAt(new Point(180, 4)).Kind, "and a press in the band beside the words still means the shape");
    });

    [TestMethod]
    public void EveryShapeMermaidsBracketsSayIsDrawnAsOneOfItsOwn()
    {
        var drawn = MermaidShapes.Nodes.Select(node => DiagramShapes.For(node.Shape)).ToList();

        Assert.AreEqual(MermaidShapes.Nodes.Count, drawn.Distinct().Count(), "no two pairs of brackets are drawn the same");
        Assert.AreEqual(DiagramShape.Rectangle, DiagramShapes.For(MermaidShape.None), "and brackets that say no shape are a plain box");
    }
}
