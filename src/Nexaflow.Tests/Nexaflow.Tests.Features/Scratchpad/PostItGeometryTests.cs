using System;
using Nexaflow.Features.Scratchpad.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Scratchpad;

/// <summary>
/// Dragging a note's edge grips. An unrotated note resizing is arithmetic nobody gets wrong; a
/// <i>rotated</i> one is where this breaks, because the note pivots about its own centre — so growing it
/// naively also slides it, and dragging its right edge pulls in a direction that is no longer "right".
/// <para>
/// The rule that holds it together is that the corner you are <b>not</b> dragging stays exactly where it
/// was on the canvas. That is what these assert, at 0°, at an angle, and at the minimum size.
/// </para>
/// </summary>
[TestClass]
public class PostItGeometryTests
{
    private static readonly NoteRect Start = new(X: 100, Y: 200, Width: 300, Height: 200);

    private static void AssertClose(double expected, double actual, string because = "") =>
        Assert.IsTrue(Math.Abs(expected - actual) < 1e-6, $"{because} (expected {expected}, got {actual})");

    /// <summary>The corner opposite the dragged edge — the one that must not move.</summary>
    private static string Opposite(string edge)
    {
        var flipped = string.Empty;
        foreach (var c in edge)
            flipped += c switch { 'N' => 'S', 'S' => 'N', 'E' => 'W', 'W' => 'E', _ => c };
        return flipped;
    }

    private static void AssertAnchorHeld(string edge, double rotation, double dx, double dy)
    {
        var before = PostItGeometry.Corner(Opposite(edge), rotation, Start);
        var after  = PostItGeometry.Corner(Opposite(edge), rotation,
                                           PostItGeometry.Resize(edge, rotation, Start, dx, dy));

        AssertClose(before.X, after.X, $"dragging {edge} at {rotation}° moved the pinned corner");
        AssertClose(before.Y, after.Y, $"dragging {edge} at {rotation}° moved the pinned corner");
    }

    // ── Unrotated ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("postit-resize")]
    public void DraggingTheEastEdge_GrowsWidthOnly_AndLeavesTheNoteWhereItIs()
    {
        var r = PostItGeometry.Resize("E", rotationDegrees: 0, Start, dragDx: 60, dragDy: 40);

        AssertClose(360, r.Width, "the east edge follows the drag");
        AssertClose(200, r.Height, "a vertical drag on a horizontal edge must not change the height");
        AssertClose(100, r.X, "the west edge is pinned");
        AssertClose(200, r.Y);
    }

    [TestMethod]
    [CoversNode("postit-resize")]
    public void DraggingTheNorthWestCorner_MovesTheOriginAndKeepsTheOppositeCornerPut()
    {
        var r = PostItGeometry.Resize("NW", rotationDegrees: 0, Start, dragDx: -50, dragDy: -30);

        AssertClose(350, r.Width);
        AssertClose(230, r.Height);
        AssertClose(50, r.X, "growing north-west has to move the top-left corner");
        AssertClose(170, r.Y);
        AssertClose(400, r.X + r.Width, "…while the bottom-right stays exactly where it was");
        AssertClose(400, r.Y + r.Height);
    }

    // ── Rotated ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("postit-resize")]
    public void ARotatedNoteResizesAlongItsOwnAxes_NotTheScreens()
    {
        // At 90° the note's local +x points down the screen, so a purely downward drag is what grows its
        // width. A screen-axes implementation would grow the height instead.
        var r = PostItGeometry.Resize("E", rotationDegrees: 90, Start, dragDx: 0, dragDy: 60);

        AssertClose(360, r.Width, "the drag ran along the note's own east direction");
        AssertClose(200, r.Height);
    }

    [TestMethod]
    [CoversNode("postit-resize")]
    [DataRow("E", 30.0)]
    [DataRow("W", 30.0)]
    [DataRow("N", -47.0)]
    [DataRow("S", 12.5)]
    [DataRow("NE", 30.0)]
    [DataRow("SW", -47.0)]
    [DataRow("NW", 12.5)]
    [DataRow("SE", 180.0)]
    public void WhicheverEdgeIsDragged_TheOppositeCornerStaysPinned(string edge, double rotation)
        => AssertAnchorHeld(edge, rotation, dx: 45, dy: -25);

    [TestMethod]
    [CoversNode("postit-resize")]
    public void AZeroDragChangesNothing()
    {
        var r = PostItGeometry.Resize("SE", rotationDegrees: 37, Start, 0, 0);

        AssertClose(Start.X, r.X, "a click without a drag must not nudge the note");
        AssertClose(Start.Y, r.Y);
        AssertClose(Start.Width, r.Width);
        AssertClose(Start.Height, r.Height);
    }

    // ── Minimum size ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("postit-resize")]
    public void ANoteCannotBeDraggedSmallerThanItsMinimum()
    {
        var r = PostItGeometry.Resize("SE", rotationDegrees: 0, Start, dragDx: -9999, dragDy: -9999);

        AssertClose(PostItGeometry.MinSize, r.Width);
        AssertClose(PostItGeometry.MinSize, r.Height);
        AssertClose(100, r.X, "clamping the size must not let the pinned corner drift either");
        AssertClose(200, r.Y);
    }

    [TestMethod]
    [CoversNode("postit-resize")]
    public void ClampingARotatedNoteStillHoldsTheAnchor()
    {
        const double rotation = 55;
        var before = PostItGeometry.Corner("NW", rotation, Start);
        var after  = PostItGeometry.Corner("NW", rotation,
                                           PostItGeometry.Resize("SE", rotation, Start, -9999, -9999));

        AssertClose(before.X, after.X);
        AssertClose(before.Y, after.Y);
    }
}
