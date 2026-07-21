using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.Features.Web;
using Nexaflow.Tests.Fixtures;
using System.Linq;

namespace Nexaflow.Tests.Features.Web;

/// <summary>
/// Tab title and breadcrumb derivation from a live URL (<see cref="WebPageChrome"/>). This is the Web
/// viewer's own title/breadcrumb leaf, and it doubles as the worked example for Core's same-tab crumb
/// navigation — hence the two covered nodes.
/// </summary>
[TestClass]
[CoversNode("web-breadcrumbs")]
[CoversNode("chrome-breadcrumb-navigate")]
public class WebPageChromeTests
{
    private const string Deep = "https://www.example.com/somedir/anotherdir/source.py?gh=213123123123123sdasd";

    // ── Title ─────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Title_NoPageTitle_UsesWebFormWithLastSegment()
        => Assert.AreEqual("web:/.../source.py", WebPageChrome.Title(Deep, null));

    [TestMethod]
    [TestCategory("Unit")]
    public void Title_LongLastSegment_ClippedToTenChars()
        => Assert.AreEqual("web:/.../abcdefghij",
            WebPageChrome.Title("https://x.com/abcdefghijklmnop", null));

    [TestMethod]
    [TestCategory("Unit")]
    public void Title_ShortPageTitle_UsedAsIs()
        => Assert.AreEqual("Example", WebPageChrome.Title(Deep, "Example"));

    [TestMethod]
    [TestCategory("Unit")]
    public void Title_LongPageTitle_TruncatedToFourteenPlusEllipsis()
        => Assert.AreEqual("ABCDEFGHIJKLMN…",
            WebPageChrome.Title(Deep, "ABCDEFGHIJKLMNOPQRSTUVWXYZ"));

    // ── Breadcrumbs ───────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Crumbs_HostThenPathSegments_QueryStripped()
    {
        var labels = WebPageChrome.Crumbs(Deep, _ => { }).Select(c => c.Label).ToArray();
        CollectionAssert.AreEqual(
            new[] { "www.example.com", "somedir", "anotherdir", "source.py" }, labels);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Crumbs_HostNavigatesToRoot_SegmentToCumulativeUrl()
    {
        string? navigated = null;
        var crumbs = WebPageChrome.Crumbs(Deep, url => navigated = url);

        crumbs[0].Navigate!();
        Assert.AreEqual("https://www.example.com/", navigated);

        crumbs[2].Navigate!();   // "anotherdir"
        Assert.AreEqual("https://www.example.com/somedir/anotherdir", navigated);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Crumbs_LocalFile_SingleFileNameCrumb()
    {
        var crumbs = WebPageChrome.Crumbs(@"C:\docs\page.html", _ => { });
        Assert.AreEqual(1, crumbs.Count);
        Assert.AreEqual("page.html", crumbs[0].Label);
    }
}
