using System.Windows.Documents;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Locate;

namespace Nexaflow.Tests.Visuals.Locate;

/// <summary>
/// What a <c>locate:</c> link says (<see cref="LocateLink"/>): the controls it names, in the order it names them —
/// and nothing at all for any other link, so a help page's ordinary links still go where they point.
/// </summary>
[TestClass]
[CoversNode("help-locate")]
public class LocateLinkTests
{
    [TestMethod]
    public void OneControl_IsTheIdItNames()
    {
        Assert.IsTrue(LocateLink.TryParse("locate:Chrome_HelpButton", out var ids));

        CollectionAssert.AreEqual(new[] { "Chrome_HelpButton" }, ids.ToArray());
    }

    [TestMethod]
    public void AChain_IsEveryIdInTheOrderWritten()
    {
        Assert.IsTrue(LocateLink.TryParse("locate:Chrome_OptionsButton, Help_SearchBox ,Help_IndexButton", out var ids));

        CollectionAssert.AreEqual(new[] { "Chrome_OptionsButton", "Help_SearchBox", "Help_IndexButton" }, ids.ToArray(),
                                  "written with or without spaces, it is the same chain");
    }

    [TestMethod]
    public void TheSchemeIsReadHoweverItIsCased_AndEscapesAreUndone()
    {
        Assert.IsTrue(LocateLink.TryParse("LOCATE:Tab%20Strip", out var ids));

        CollectionAssert.AreEqual(new[] { "Tab Strip" }, ids.ToArray());
    }

    [TestMethod]
    public void AnythingElse_IsNotALocateLink()
    {
        foreach (var url in new[] { null, "", "   ", "help:Text", "https://example.com/locate:x", "locate:", "locate:,,", "locates:X" })
            Assert.IsFalse(LocateLink.TryParse(url, out var ids) || ids.Count > 0, $"'{url ?? "null"}' names no control");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void ARenderedLocateLink_IsMarkedAsOne_AndEveryOtherLinkIsLeftAlone() => UiThread.Run(() =>
    {
        const string tooltip = "Show me where this is";
        var locate = new Hyperlink(new Run("the Help button"));
        var plain  = new Hyperlink(new Run("the manual"));

        LocateLink.Decorate(locate, "locate:Chrome_HelpButton", tooltip);
        LocateLink.Decorate(plain, "https://example.com", tooltip);

        var marked = Text(locate);
        StringAssert.EndsWith(marked, "the Help button", "the link still reads as it was written");
        Assert.IsTrue(marked.Length > "the Help button".Length, "…behind a pin saying it points at the screen");
        Assert.AreEqual(tooltip, locate.ToolTip);

        Assert.AreEqual("the manual", Text(plain), "an ordinary link is untouched");
        Assert.IsNull(plain.ToolTip);
    });

    private static string Text(Hyperlink link) => new TextRange(link.ContentStart, link.ContentEnd).Text;
}
