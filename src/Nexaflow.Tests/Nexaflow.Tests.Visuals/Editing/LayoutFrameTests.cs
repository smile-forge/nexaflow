using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// A piece drawn in a frame of its own — set smaller, or turned (<see cref="LayoutPaint"/>): everything it holds is painted,
/// pressed, caretted and washed in that frame, however deep inside it, because the painter and every question asked of the
/// layout go through the same frame.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("layout-frames")]
public class LayoutFrameTests
{
    //  The source the words stand for:  pie "Dogs" : 386
    //  offsets                          0   4      11
    private static readonly TestPart LabelPart = new(4, 4);
    private static readonly TestPart ValuePart = new(11, 3);

    private const double Half = 0.5;
    private static readonly Point At = new(100, 40);

    private static FormattedText Set(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Black, 1.0);

    /// <summary>A label and a value, each a run of text, and a square standing in its own shape, inside one piece.</summary>
    private static LayoutTree Drawing()
    {
        var build = new LayoutBuilder();
        build.Open("drawing", new TestPart(0, 14), stops: Stops.None);

        LayoutText.Words(build, Set("Dogs"), new Point(10, 0), 200, TextAlignment.Left, LabelPart, "label");
        LayoutText.Words(build, Set("386"), new Point(80, 0), 200, TextAlignment.Left, ValuePart, "value");

        build.Open("square", new TestPart(0, 3), new Point(0, 40), gathers: false);
        build.Covers(new Rect(0, 0, 40, 40));
        build.Occupies(new EllipseGeometry(new Rect(0, 0, 40, 40)));
        build.Close();

        build.Close();
        return build.Seal();
    }

    /// <summary>The drawing set down at <see cref="At"/> in a piece painted as <paramref name="paints"/> says.</summary>
    private static Piece Page(LayoutPaint paints)
    {
        var build = new LayoutBuilder();
        build.Open("page", stops: Stops.None);
        build.Open("held", at: At, stops: Stops.None, paints: paints);
        build.Graft(Drawing());
        build.Close();
        build.Close();
        return build.Seal().Root;
    }

    private static Piece Kind(Piece root, string kind) => root.SelfAndDescendants().Single(piece => piece.Kind == kind);

    /// <summary>Where a point of the unscaled drawing lands, the drawing set smaller about its anchor at <see cref="At"/>.</summary>
    private static Point Scaled(Point inDrawing) => new(At.X + (inDrawing.X * Half), At.Y + (inDrawing.Y * Half));

    [TestMethod]
    public void APieceSetSmallerReachesAsFarAsWhatItHoldsLands() => UiThread.Run(() =>
    {
        var whole = Drawing().Root.Bounds;
        var held = Kind(Page(new LayoutPaint(Scale: Half)), "held");

        Assert.AreEqual(At.X + (whole.X * Half), held.Bounds.X, 0.001);
        Assert.AreEqual(whole.Width * Half, held.Bounds.Width, 0.001, "as wide as what it holds, drawn at its scale");
        Assert.AreEqual(whole.Height * Half, held.Bounds.Height, 0.001);
        Assert.AreEqual(whole.Width * Half, held.Parent.Bounds.Width, 0.001, "and what holds it holds no more than that");
    });

    [TestMethod]
    public void WhatItHoldsIsPaintedWhereItIsSaidToBe() => UiThread.Run(() =>
    {
        var root = Page(new LayoutPaint(Scale: Half));

        var painted = new DrawingGroup();
        using (var dc = painted.Open()) LayoutPainter.Paint(dc, root, Brushes.Black);

        var square = Kind(root, "square").Bounds;
        Assert.AreEqual(Scaled(new Point(0, 40)).Y, square.Y, 0.001, "the square stands where the scale puts it");
        Assert.IsTrue(Rect.Inflate(Kind(root, "held").Bounds, 1, 1).Contains(painted.Bounds),
                      $"everything painted, {painted.Bounds}, is inside where it is said to be, {Kind(root, "held").Bounds}");
    });

    [TestMethod]
    public void APressMeansTheLetterItLandedOn_WhereTheLettersAreDrawn() => UiThread.Run(() =>
    {
        var root = Page(new LayoutPaint(Scale: Half));
        var value = Kind(Drawing().Root, "value");
        var anchor = value.Bounds.TopLeft;

        foreach (var index in new[] { 0, 1, 2 })
        {
            var letter = value.Words!.Covers(index, index + 1);
            var middle = anchor.Y + letter.Y + (letter.Height / 2);

            Assert.AreEqual(ValuePart.Start + index, root.OffsetAt(Scaled(new Point(anchor.X + letter.X + (letter.Width * 0.25), middle))),
                            $"the left half of letter {index}, drawn smaller, means before it");
            Assert.AreEqual(ValuePart.Start + index + 1, root.OffsetAt(Scaled(new Point(anchor.X + letter.X + (letter.Width * 0.75), middle))),
                            $"and its right half after it");
        }
    });

    [TestMethod]
    public void TheCaretAndTheWashAreDrawnAtTheScale() => UiThread.Run(() =>
    {
        var root = Page(new LayoutPaint(Scale: Half));
        var value = Kind(Drawing().Root, "value");
        var caret = value.Words!.Caret(1);

        var bar = root.CaretRect(ValuePart.Start + 1);
        Assert.AreEqual(Scaled(new Point(value.Bounds.X + caret.X, 0)).X, bar.X, 0.001, "between the first two letters, where they are drawn");
        Assert.AreEqual(caret.Height * Half, bar.Height, 0.001, "and as tall as they are drawn");

        var letter = value.Words!.Covers(1, 2);
        var washed = root.RangeRects(ValuePart.Start + 1, 1).Single();
        Assert.AreEqual(letter.Width * Half, washed.Width, 0.001, "one letter washed, as wide as it is drawn");
    });

    [TestMethod]
    public void AShapeIsPressedAndWashedWhereItIsDrawn() => UiThread.Run(() =>
    {
        var root = Page(new LayoutPaint(Scale: Half));
        var square = Kind(root, "square");

        var middle = Scaled(new Point(20, 60));
        Assert.IsTrue(square.Squarely(middle), "the middle of the circle, where it is drawn, is in it");
        Assert.IsFalse(square.Squarely(Scaled(new Point(2, 42))), "and the corner of its box, outside the circle, is not");
        Assert.IsFalse(square.Squarely(new Point(At.X + 20, At.Y + 60)), "nor where it would be drawn at its full size");

        var shape = square.OnPage()!;
        Assert.AreEqual(20, shape.Bounds.Width, 0.001, "its shape is the size it is drawn");
        Assert.IsTrue(shape.FillContains(middle));
    });

    [TestMethod]
    public void WhatATurnedPieceHolds_IsPressedWhereItIsDrawn() => UiThread.Run(() =>
    {
        var root = Page(new LayoutPaint([new RotateTransform(90)]));
        var value = Kind(Drawing().Root, "value");
        var letter = value.Words!.Covers(1, 2);

        // Turned a quarter about the anchor, a point (x, y) of the drawing lands at (-y, x) from it.
        var inDrawing = new Point(value.Bounds.X + letter.X + (letter.Width * 0.25), value.Bounds.Y + letter.Y + (letter.Height / 2));
        var press = new Point(At.X - inDrawing.Y, At.Y + inDrawing.X);

        Assert.AreEqual(ValuePart.Start + 1, root.OffsetAt(press), "a press on a letter of something a turned piece holds means that letter");
    });
}
