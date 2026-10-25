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
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Layout.Mermaid;

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
        foreach (var shape in Shapes.Where(PortableDiagramShapes.Worded))
            foreach (var words in new[] { new LayoutSize(60, 14), new LayoutSize(12, 30), new LayoutSize(220, 16) })
            {
            var size = PortableDiagramShapes.Around(shape, words, pad: 6);
            var bounds = new LayoutRect(new LayoutPoint(10, 20), size);
                var inside = PortableDiagramShapes.Inside(shape, bounds);

                Assert.IsTrue(inside.Width >= words.Width - 1e-6 && inside.Height >= words.Height - 1e-6,
                              $"{shape}: {words} fits in {inside}");

                var outline = PortableDiagramShapes.Outline(WpfHandles.Medium, shape, bounds);
                var room = new LayoutRect(inside.X + (inside.Width / 2) - (words.Width / 2) + 0.5, inside.Y + (inside.Height / 2) - (words.Height / 2) + 0.5,
                                    Math.Max(0, words.Width - 1), Math.Max(0, words.Height - 1));

                foreach (var corner in new[] { room.TopLeft, room.TopRight, room.BottomLeft, room.BottomRight })
                    Assert.IsTrue(outline.Contains(corner), $"{shape}: the words' corner {corner} is inside its outline");
            }
    });

    [TestMethod]
    public void AShapesOutlineFillsItsBounds() => UiThread.Run(() =>
    {
        var bounds = new LayoutRect(5, 5, 120, 60);

        foreach (var shape in Shapes)
        {
            var outline = PortableDiagramShapes.Outline(WpfHandles.Medium, shape, bounds);
            Assert.IsTrue(outline.Geometry().IsFrozen, $"{shape} is frozen, so it can be drawn from any thread");
            Assert.AreEqual(bounds.Width, outline.Bounds.Width, 1, $"{shape}: as wide as its bounds");
            Assert.AreEqual(bounds.Height, outline.Bounds.Height, 1.5, $"{shape}: as tall as its bounds");
        }
    });

    [TestMethod]
    public void AConnectorMeetsAShapeAtItsOutline() => UiThread.Run(() =>
    {
        var bounds = new LayoutRect(0, 0, 120, 60);
        var edge = new Pen(Brushes.Black, 2);

        foreach (var shape in new[]
                 {
                     DiagramShape.Rectangle, DiagramShape.Circle, DiagramShape.Diamond, DiagramShape.Hexagon, DiagramShape.Asymmetric,
                     DiagramShape.Parallelogram, DiagramShape.ParallelogramAlt, DiagramShape.Trapezoid, DiagramShape.TrapezoidAlt, DiagramShape.Card,
                     DiagramShape.NotchedPentagon, DiagramShape.SlopedRectangle, DiagramShape.Triangle, DiagramShape.FlippedTriangle, DiagramShape.Folder,
                     DiagramShape.SmallCircle, DiagramShape.CrossedCircle,
                 })
        {
            var outline = PortableDiagramShapes.Outline(WpfHandles.Medium, shape, bounds).Geometry();

            for (var turn = 0; turn < 16; turn++)
            {
                var angle = turn * Math.PI / 8;
                var toward = new LayoutPoint(60 + (500 * Math.Cos(angle)), 30 + (500 * Math.Sin(angle)));
                var met = PortableDiagramShapes.Edge(shape, bounds, toward);

                Assert.IsTrue(outline.StrokeContains(edge, met.ToWpf()), $"{shape}: a line toward {toward.ToWpf().ToString(CultureInfo.InvariantCulture)} meets it at {met}, on its outline");
            }
        }
    });

    [TestMethod]
    public void AShapeStandsInItsOutline_WithItsWordsInTheMiddle() => UiThread.Run(() =>
    {
        var words = Words("Decide", new TestPart(0, 6));
        var bounds = new LayoutRect(new LayoutPoint(40, 30), PortableDiagramShapes.Around(DiagramShape.Diamond, new LayoutSize(words.Width, words.Height), pad: 6));

        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");

        // Something under it, so a press that misses the diamond has something else to mean.
        build.Open("Page", new TestPart(20, 1), stops: PortableStops.None);
        build.Draw(new PortableRuleMark(new LayoutRect(0, 0, 400, 300), Brushes.White.Ink()));
        build.Close();

        PortableDiagramShapes.Draw(build, "Node", new TestPart(0, 6), DiagramShape.Diamond, bounds, Brushes.White.Ink(), new PortableDiagramStroke(Brushes.Black.Ink()), words);
        build.Close();

        var root = build.Seal().Root;
        var node = root.SelfAndDescendants().Single(piece => piece.Kind == "Node");
        var said = root.SelfAndDescendants().Single(piece => piece.Kind == MermaidPiece.Words);

        var inside = root.PieceAt(new LayoutPoint(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 5)));
        Assert.IsTrue(inside.Kind == MermaidPiece.Shape && inside.Parent == node, $"a press inside the diamond means it, not the {inside.Kind}");
        Assert.AreEqual("Page", root.PieceAt(new LayoutPoint(bounds.X + 2, bounds.Y + 2)).Kind, "and one in the corner of its bounds, outside it, means what is behind");
        Assert.AreEqual(bounds.X + (bounds.Width / 2), said.Bounds.X + (said.Bounds.Width / 2), 1, "the words are in the middle across");
        Assert.AreEqual(MermaidPiece.Words, root.PieceAt(new LayoutPoint(said.Bounds.Right - 1, said.Bounds.Y + (said.Bounds.Height / 2))).Kind, "and a press on the words means them, not the shape under them");
    });

    /// <summary>Words worked out, which stand for nothing.</summary>
    internal static PortableDiagramWords Words(string text, ISourcePart? part = null)
    {
        var set = WpfHandles.Medium.Shape(text, new Typeface("Segoe UI"), 12, Brushes.Black, null, CultureInfo.InvariantCulture);
        var letter = WpfHandles.Medium.Shape("x", new Typeface("Segoe UI"), 12, Brushes.Black, null, CultureInfo.InvariantCulture);
        return new PortableDiagramWords(set, part, hole: null, letter, Brushes.Black.Ink(), maps: part is not null, writes: part is not null);
    }

    [TestMethod]
    public void AShapeWithWordsPlacedInItStandsLessThoseWordsAndWhatIsDrawnOverIt() => UiThread.Run(() =>
    {
        var bounds = new LayoutRect(0, 0, 200, 120);
        var title = Words("Title", new TestPart(0, 5));
        var covered = new RectangleGeometry(new Rect(20, 60, 160, 40)).Shape();

        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        PortableDiagramShapes.Draw(build, "Column", new TestPart(10, 3), DiagramShape.Rectangle, bounds, Brushes.White.Ink(), new PortableDiagramStroke(Brushes.Black.Ink()),
                           [(title, new LayoutPoint(10, 10), "Title")], covered);

        // A card drawn after the column, where the column was told something is drawn over it: a press there is the card's,
        // though the column came first.
        build.Open("Card", new TestPart(20, 2), stops: PortableStops.None);
        build.Draw(new PortableGeometryMark(covered, Brushes.Gray.Ink(), null));
        build.Occupies(covered);
        build.Close();
        build.Close();

        var root = build.Seal().Root;

        Assert.AreEqual("Title", root.PieceAt(new LayoutPoint(12, 10 + (title.Height / 2))).Kind, "a press on the words means them");
        Assert.AreEqual(MermaidPiece.Shape, root.PieceAt(new LayoutPoint(100, 45)).Kind, "one elsewhere in the shape means the shape");
        Assert.AreEqual("Card", root.PieceAt(new LayoutPoint(100, 80)).Kind, "and one where something else is drawn over it means that");
    });

    [TestMethod]
    public void AShapesNameCanBeSetInABandOfItsOwnAcrossTheTopOfIt() => UiThread.Run(() =>
    {
        var bounds = new LayoutRect(0, 0, 200, 120);
        var title = Words("Title", new TestPart(0, 5));

        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        PortableDiagramShapes.Draw(build, "Group", new TestPart(10, 3), DiagramShape.Rounded, bounds, Brushes.White.Ink(), new PortableDiagramStroke(Brushes.Black.Ink()),
                           [(title, new LayoutPoint(10, 6), "Title")], band: Brushes.Navy.Ink());
        build.Close();

        var root = build.Seal().Root;
        var shape = root.SelfAndDescendants().Single(piece => piece.Kind == MermaidPiece.Shape);
        var marks = shape.Marks.ToArray().OfType<PortableGeometryMark>().ToList();
        var band = marks.Single(mark => mark.Fill == Brushes.Navy.Ink()).Shape.Bounds;

        Assert.AreEqual(bounds.Top, band.Top, 0.5, "the band runs from the top of the shape");
        Assert.AreEqual(bounds.Width, band.Width, 0.5, "and right across it");
        Assert.AreEqual(6 + title.Height + 6, band.Height, 0.5, "down past the words by as much air again as is over them");
        Assert.IsTrue(marks.Any(mark => mark.Shape.Geometry() is LineGeometry line && Math.Abs(line.StartPoint.Y - band.Bottom) < 0.5 && mark.Stroke?.Ink == Brushes.Black.Ink()),
                      "with a rule under it in the outline's ink");
        Assert.AreEqual(Brushes.White.Ink(), marks[0].Fill, "over the shape's own fill");
        Assert.AreEqual(MermaidPiece.Shape, root.PieceAt(new LayoutPoint(180, 4)).Kind, "and a press in the band beside the words still means the shape");
    });

    [TestMethod]
    public void EveryShapeMermaidNamesIsDrawnAsOneOfItsOwn()
    {
        var named = Enum.GetValues<MermaidShape>().Except([MermaidShape.None, MermaidShape.Text]).ToList();
        var drawn = named.Select(PortableDiagramShapes.For).ToList();

        Assert.AreEqual(named.Count, drawn.Distinct().Count(), "no two shapes are drawn the same");
        Assert.AreEqual(DiagramShape.Rectangle, PortableDiagramShapes.For(MermaidShape.None), "and brackets that say no shape are a plain box");
    }

    /// <summary>Mermaid's small markers are drawn at a size of their own without their words, and the solid ones filled in their ink.</summary>
    [TestMethod]
    public void AMarkerIsDrawnWithoutItsWords_TheSolidOnesInTheirInk() => UiThread.Run(() =>
    {
        foreach (var shape in new[] { DiagramShape.SmallCircle, DiagramShape.FilledCircle, DiagramShape.Fork, DiagramShape.Bolt, DiagramShape.Hourglass })
        {
            var size = PortableDiagramShapes.Around(shape, new LayoutSize(200, 40), pad: 6);
            Assert.IsTrue(size.Width < 70 && size.Height < 70, $"{shape} is its own size, {size}, whatever its words");

            var build = new PortableBuilder(WpfMedium.OnThisThread);
            build.Open("page");
            PortableDiagramShapes.Draw(build, "Node", new TestPart(0, 6), shape, new LayoutRect(new LayoutPoint(10, 10), size), Brushes.White.Ink(),
                               new PortableDiagramStroke(Brushes.Navy.Ink()), Words("Unseen", new TestPart(0, 6)));
            build.Close();

            var root = build.Seal().Root;
            Assert.IsFalse(root.SelfAndDescendants().Any(piece => piece.Kind == MermaidPiece.Words), $"{shape} draws no words");

            var fill = root.SelfAndDescendants().Single(piece => piece.Kind == MermaidPiece.Shape).Marks.ToArray().OfType<PortableGeometryMark>().First().Fill;
            Assert.AreEqual(shape is DiagramShape.FilledCircle or DiagramShape.Fork ? Brushes.Navy.Ink() : Brushes.White.Ink(), fill, $"{shape}'s fill");
        }
    });

    /// <summary>A brace marks the words beside it: nothing is filled, but a press anywhere in its bounds still means it.</summary>
    [TestMethod]
    public void ABraceIsNotFilled_ButStandsInAllItsBounds() => UiThread.Run(() =>
    {
        var bounds = new LayoutRect(0, 0, 120, 40);

        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        PortableDiagramShapes.Draw(build, "Node", new TestPart(0, 6), DiagramShape.Braces, bounds, Brushes.White.Ink(), new PortableDiagramStroke(Brushes.Black.Ink()));
        build.Close();

        var root = build.Seal().Root;
        var shape = root.SelfAndDescendants().Single(piece => piece.Kind == MermaidPiece.Shape);

        Assert.IsNull(shape.Marks.ToArray().OfType<PortableGeometryMark>().First().Fill, "no fill behind the words");
        Assert.AreEqual(MermaidPiece.Shape, root.PieceAt(new LayoutPoint(60, 20)).Kind, "and a press between the braces means the shape");
    });
}
