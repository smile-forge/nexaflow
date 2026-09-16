using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Pieces that stand in a shape rather than a box (<see cref="LayoutBuilder.Occupies"/>): a press means the shape it
/// lands in, a press between shapes means the nearer one, a marquee takes what its shapes reach, and a selection is
/// washed as the shapes it holds.
///
/// <para>
/// The trees are two triangles filling the same square from opposite corners, with a gap along the diagonal — the worst
/// case for boxes, since both pieces have the very same box and every press anywhere in it lands in both.
/// </para>
/// </summary>
[TestClass]
[CoversNode("layout-regions")]
public class LayoutRegionTests
{
    //  offsets   upper 0..5, lower 6..11, the whole thing 0..11
    private static readonly TestPart UpperPart = new(0, 5);
    private static readonly TestPart LowerPart = new(6, 5);

    /// <summary>The upper-left triangle, short of the diagonal.</summary>
    private static readonly Geometry Upper = Geometry.Parse("M0,0 L90,0 L0,90 Z");

    /// <summary>The lower-right triangle, short of the diagonal from the other side.</summary>
    private static readonly Geometry Lower = Geometry.Parse("M100,10 L100,100 L10,100 Z");

    private static LayoutTree Wedges(bool shaped = true, TestPart? label = null)
    {
        var build = new LayoutBuilder();
        build.Open("pie", new TestPart(0, 11));

        foreach (var (part, shape) in new[] { (UpperPart, Upper), (LowerPart, Lower) })
        {
            build.Open("wedge", part);
            build.Draw(GeometryMark.Filled(shape));
            if (shaped) build.Occupies(shape);
            build.Close();
        }

        if (label is not null) build.Leaf(new Rect(110, 0, 30, 10), label, "label");

        build.Close();
        return build.Seal();
    }

    [TestMethod]
    public void TwoPiecesWithOneBoxAreTheSameBoxToEachOther()
    {
        // What the rest of this is about: without a shape, their boxes cannot tell them apart.
        var wedges = Wedges(shaped: false).Root.Children.ToList();
        Assert.AreEqual(wedges[0].Bounds, new Rect(0, 0, 90, 90));
        Assert.IsTrue(wedges[1].Bounds.Contains(new Point(20, 20)), "the lower wedge's box reaches right into the upper wedge");
    }

    [TestMethod]
    public void APressMeansTheShapeItLandsIn()
    {
        var root = Wedges().Root;

        Assert.AreEqual(LowerPart.Start, root.PieceAt(new Point(80, 80)).Sits().Start);
        Assert.AreEqual(UpperPart.Start, root.PieceAt(new Point(20, 20)).Sits().Start);
        Assert.AreEqual(UpperPart.Start, root.PieceAt(new Point(60, 25)).Sits().Start, "inside both boxes, and in the upper wedge");
    }

    [TestMethod]
    public void APressBetweenShapesMeansTheNearerShape()
    {
        var root = Wedges().Root;

        Assert.AreEqual(UpperPart.Start, root.PieceAt(new Point(47, 47)).Sits().Start, "just past the upper wedge's edge");
        Assert.AreEqual(LowerPart.Start, root.PieceAt(new Point(53, 53)).Sits().Start, "just short of the lower wedge's edge");
    }

    [TestMethod]
    public void AMarqueeTakesOnlyTheShapesItReaches()
    {
        var root = Wedges().Root;

        var taken = root.PiecesIn(new Rect(75, 75, 10, 10));
        Assert.AreEqual(LowerPart.Start, taken.Single().Sits().Start);
    }

    [TestMethod]
    public void ASelectedShapeIsWashedAsItself()
    {
        var root = Wedges().Root;
        var wash = root.Wash([new EditRange(UpperPart.Start, UpperPart.Length)], pad: 2);

        Assert.IsTrue(wash.FillContains(new Point(20, 20)), "the wedge chosen is washed");
        Assert.IsTrue(wash.FillContains(new Point(45, 46)), "and the pad around its edge");
        Assert.IsFalse(wash.FillContains(new Point(80, 80)), "and its neighbour, which shares its box, is not");
        Assert.AreEqual(0, root.RangeRects(UpperPart.Start, UpperPart.Length).Count, "a shape is no rectangle's business");
    }

    [TestMethod]
    public void AShapeAndABoxChosenTogetherAreWashedTogether()
    {
        // A label written inside the upper wedge's stretch, drawn off to the side of the pie.
        var root = Wedges(label: new TestPart(1, 2)).Root;
        var wash = root.Wash([new EditRange(UpperPart.Start, UpperPart.Length)], pad: 2);

        Assert.IsTrue(wash.FillContains(new Point(20, 20)));
        Assert.IsTrue(wash.FillContains(new Point(125, 5)), "the label belonging to it");
        Assert.IsFalse(wash.FillContains(new Point(80, 80)));
    }

    [TestMethod]
    public void AShapeGoesWhereverItsTreeIsPutDown()
    {
        var build = new LayoutBuilder();
        build.Open("page");
        build.Graft(Wedges(), new Point(200, 40));
        build.Close();
        var root = build.Seal().Root;

        Assert.AreEqual(LowerPart.Start, root.PieceAt(new Point(280, 120)).Sits().Start);
        Assert.AreEqual(UpperPart.Start, root.PieceAt(new Point(220, 60)).Sits().Start);

        var wash = root.Wash([new EditRange(LowerPart.Start, LowerPart.Length)], pad: 0);
        Assert.IsTrue(wash.FillContains(new Point(280, 120)), "washed where it landed");
        Assert.IsFalse(wash.FillContains(new Point(80, 80)), "not where it was built");
    }
}
