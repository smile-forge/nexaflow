using System;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Layout.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The marks a chart tells its groups apart by: a few pixels across, holding nothing, and still legible
/// at that size.
///
/// <para>
/// A glyph is not a <see cref="DiagramShape"/>, and the difference is what these hold. A node shape is
/// sized around words and is held to holding them; a glyph fills the box it is given and has no inside
/// at all. Three of them draw as a node shape, and that is a reuse rather than a second definition.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramGlyphTests
{
    private static readonly DiagramGlyph[] Glyphs = Enum.GetValues<DiagramGlyph>();

    private static readonly LayoutRect Box = new(10, 20, 40, 40);

    [TestMethod]
    public void EveryGlyphFillsTheBoxItIsGiven() => UiThread.Run(() =>
    {
        // Which is what makes two glyphs at the same place the same size as each other.
        foreach (var glyph in Glyphs)
        {
            var bounds = PortableDiagramGlyphs.Outline(WpfHandles.Medium, glyph, Box).Bounds;

            Assert.AreEqual(Box.Left, bounds.Left, 0.5, $"{glyph} starts where it was asked to");
            Assert.AreEqual(Box.Top, bounds.Top, 0.5, $"{glyph} starts where it was asked to");
            Assert.AreEqual(Box.Width, bounds.Width, 0.5, $"{glyph} is as wide as it was asked to be");
            Assert.AreEqual(Box.Height, bounds.Height, 0.5, $"{glyph} is as tall as it was asked to be");
        }
    });

    [TestMethod]
    public void EveryGlyphIsSolidAtItsMiddle() => UiThread.Run(() =>
    {
        // A mark with a hole where the point is would put the point somewhere it is not.
        foreach (var glyph in Glyphs)
            Assert.IsTrue(PortableDiagramGlyphs.Outline(WpfHandles.Medium, glyph, Box).Contains(new LayoutPoint(30, 40)),
                          $"{glyph} covers the point it stands at");
    });

    [TestMethod]
    public void TheGlyphsAreHandedOutInOrderAndStartAgainRatherThanRunOut() => UiThread.Run(() =>
    {
        Assert.AreEqual(DiagramGlyph.Circle, PortableDiagramGlyphs.At(0));
        Assert.AreEqual(PortableDiagramGlyphs.At(0), PortableDiagramGlyphs.At(PortableDiagramGlyphs.Order.Count));
        Assert.AreEqual(PortableDiagramGlyphs.At(1), PortableDiagramGlyphs.At(PortableDiagramGlyphs.Order.Count + 1));
    });

    [TestMethod]
    public void EveryGlyphCanBeNamed()
    {
        // So a block asking for one by name reaches all of them.
        foreach (var glyph in Glyphs)
            Assert.IsTrue(PortableDiagramGlyphs.Order.Contains(glyph), $"{glyph} is in the order");

        Assert.AreEqual(DiagramGlyph.Triangle, PortableDiagramGlyphs.Named("triangle"));
        Assert.AreEqual(DiagramGlyph.TriangleDown, PortableDiagramGlyphs.Named("triangle-down"));
        Assert.AreEqual(DiagramGlyph.Cross, PortableDiagramGlyphs.Named("X"));
        Assert.AreEqual(DiagramGlyph.Plus, PortableDiagramGlyphs.Named("+"));
        Assert.IsNull(PortableDiagramGlyphs.Named("sunburst"));
    }

    [TestMethod]
    public void ACrossReachesTheCornersAndAPlusDoesNot() => UiThread.Run(() =>
    {
        // Which is the whole of the difference between the two, and why both are worth having.
        var cross = PortableDiagramGlyphs.Outline(WpfHandles.Medium, DiagramGlyph.Cross, Box);
        var plus = PortableDiagramGlyphs.Outline(WpfHandles.Medium, DiagramGlyph.Plus, Box);

        var corner = new LayoutPoint(Box.Left + 2, Box.Top + 2);

        Assert.IsTrue(cross.Contains(corner));
        Assert.IsFalse(plus.Contains(corner));
    });
}
