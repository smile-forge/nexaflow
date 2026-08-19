using Nexaflow.Features.Pdf.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// One row of the Contents panel. Small, but it owns the two things that decide whether the panel stays
/// usable on a real document: how far a row indents, and whether it offers to jump.
/// </summary>
[TestClass]
[CoversNode("pdf-panel-toc")]
public class PdfOutlineItemTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Indent_GrowsWithNesting()
    {
        Assert.AreEqual(0, new PdfOutlineItem("Root", 0, 1).IndentWidth);
        Assert.IsTrue(new PdfOutlineItem("Child", 1, 1).IndentWidth
                    > new PdfOutlineItem("Root", 0, 1).IndentWidth);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Indent_StopsGrowing_SoADeepOutlineCannotPushTitlesOffThePanel()
    {
        // Nesting depth in a real document is bounded by nothing. Uncapped, a dozen-level outline indents its
        // titles past the width of a 300px panel and the rows become unreadable — the level is still legible
        // from the rows above, so the indent is what gives.
        var deep = new PdfOutlineItem("Very nested", 40, 1).IndentWidth;
        var six  = new PdfOutlineItem("Six deep", 6, 1).IndentWidth;

        Assert.AreEqual(six, deep);
        Assert.IsTrue(deep < 120, $"indent must stay well inside a narrow panel (was {deep})");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PageLabelAndCopyText_SayNothingAboutAPageThatIsNotThere()
    {
        // A grouping heading has no destination. It still copies, but it must not print "p. 0" or claim a
        // page it cannot jump to.
        var group = new PdfOutlineItem(PdfSamples.OutlineGroupTitle, 0, null);

        Assert.AreEqual(string.Empty, group.PageLabel);
        Assert.AreEqual(PdfSamples.OutlineGroupTitle, group.CopyWithPage);
        Assert.IsFalse(group.CanJump, "nothing to jump to until a page says otherwise");

        var withPage = new PdfOutlineItem("Chapter", 0, 12);
        Assert.AreEqual("p. 12", withPage.PageLabel);
        StringAssert.Contains(withPage.CopyWithPage, "12");
    }
}
