using System.Collections.Generic;
using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// How the bar turns a crumb into what it draws: as its page stated it, restated by each display filter in turn, then
/// shortened — middle parts first for a crumb of many parts, letters for a middle crumb of one.
/// </summary>
[TestClass]
[CoversNode("chrome-breadcrumb-display")]
public sealed class BreadcrumbDrawingTests
{
    /// <summary>Restates a place crumb as its path's parts, as a feature's filter would.</summary>
    private sealed class IntoParts : IBreadcrumbDisplayFilter
    {
        public int Order => 0;

        public void Apply(BreadcrumbSegment crumb, BreadcrumbDisplay display)
        {
            if (crumb.Content is not PlaceContent place) return;
            display.Parts     = place.Path.Split('\\');
            display.Separator = "\\";
        }
    }

    /// <summary>Refines what the filter before it produced: copies the label it was handed, with a scheme.</summary>
    private sealed class Addressed : IBreadcrumbDisplayFilter
    {
        public int Order => 1;

        public void Apply(BreadcrumbSegment crumb, BreadcrumbDisplay display)
        {
            if (crumb.Content is PlaceContent) display.CopyText = "file:" + display.Text;
        }
    }

    private static BreadcrumbSegment Place(string path, PlaceNaming naming = PlaceNaming.WholePath)
        => new() { Label = path, Path = path, Content = new PlaceContent(path, naming) };

    [TestMethod]
    public void ACrumbNoFilterKnows_ReadsAsItsPageStatedIt()
    {
        var display = BreadcrumbDrawing.Compose(new BreadcrumbSegment { Label = "6 images" }, [new IntoParts()]);

        CollectionAssert.AreEqual(new[] { "6 images" }, (System.Collections.ICollection)display.Parts);
        Assert.AreEqual("6 images", display.Text);
        Assert.AreEqual("6 images", display.ToolTip);
        Assert.IsNull(display.CopyText, "a crumb naming no location has nothing to copy");
    }

    [TestMethod]
    public void TheFiltersAreStacked_EachSeeingWhatTheOnesBeforeItLeft()
    {
        var display = BreadcrumbDrawing.Compose(Place(@"D:\work\temp"), [new IntoParts(), new Addressed()]);

        Assert.AreEqual(@"D:\work\temp", display.Text);
        Assert.AreEqual(@"file:D:\work\temp", display.CopyText, "the second filter read the parts the first produced");
    }

    [TestMethod]
    public void AManyPartCrumb_LosesItsMiddleParts_NearestTheRootFirst_WhereverItStands()
    {
        var display = BreadcrumbDrawing.Compose(Place(@"D:\a\b\temp"), [new IntoParts()]);

        Assert.AreEqual(2, BreadcrumbDrawing.DeepestLevel(display, atEnd: true), "its root and its last part still say where it is");
        Assert.AreEqual(@"D:\a\b\temp", BreadcrumbDrawing.Shortened(display, 0));
        Assert.AreEqual(@"D:\…\b\temp", BreadcrumbDrawing.Shortened(display, 1));
        Assert.AreEqual(@"D:\…\temp",   BreadcrumbDrawing.Shortened(display, 2));
    }

    [TestMethod]
    public void ACrumbOfOnePart_LosesLettersInTheMiddle_AndStaysWholeAtAnEnd()
    {
        var display = BreadcrumbDrawing.Compose(new BreadcrumbSegment { Label = "Documents" }, []);

        Assert.AreEqual(3, BreadcrumbDrawing.DeepestLevel(display, atEnd: false));
        Assert.AreEqual(0, BreadcrumbDrawing.DeepestLevel(display, atEnd: true), "the first and last crumb anchor the trail");
        Assert.AreEqual("Doc…", BreadcrumbDrawing.Shortened(display, 1));
        Assert.AreEqual("D…",   BreadcrumbDrawing.Shortened(display, 3));
    }
}
