using System.Globalization;
using Nexaflow.Core.Converters;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shell's rule for how long a tab label may be. Applied by the tab strip for every feature, so a tab is
/// named the same way whoever opened it — and applied at display time, because <c>Page.Title</c> is also read
/// by quick-open, ribbon pinning, session capture and the AI context summary, all of which want the real name.
/// The hover tooltip is what gives the full name back.
/// </summary>
[TestClass]
[CoversNode("chrome-tab-title")]
public class TabTitleTests
{
    // ── The limit ─────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("Documents")]
    [DataRow("Downloads")]
    [DataRow("This PC")]
    [DataRow("")]
    public void ShortTitle_IsLeftAlone(string title)
        => Assert.AreEqual(title, TabTitle.Shorten(title));

    [TestMethod]
    public void TitleExactlyAtTheLimit_IsLeftAlone()
    {
        var title = new string('x', TabTitle.MaxLength);

        Assert.AreEqual(title, TabTitle.Shorten(title), "the limit is inclusive");
    }

    [TestMethod]
    public void LongTitle_IsShortenedToTheLimit_EllipsisIncluded()
    {
        var shortened = TabTitle.Shorten(new string('x', 40));

        Assert.AreEqual(TabTitle.MaxLength, shortened.Length,
            "the ellipsis replaces a character rather than being appended past the limit");
        StringAssert.EndsWith(shortened, "…");
    }

    [TestMethod]
    [DataRow(16)]
    [DataRow(20)]
    [DataRow(64)]
    [DataRow(400)]
    public void NoTitleEverExceedsTheLimit(int length)
        => Assert.IsTrue(TabTitle.Shorten(new string('a', length)).Length <= TabTitle.MaxLength);

    [TestMethod]
    public void ShortenedTitle_KeepsTheStartOfTheName()
    {
        // The leading characters are what distinguish one tab from another; the tail is what to drop.
        var shortened = TabTitle.Shorten("ProjectAlphaReportsQ4");

        StringAssert.StartsWith(shortened, "ProjectAlpha");
    }

    [TestMethod]
    public void TrailingSpace_IsNotLeftSittingBeforeTheEllipsis()
    {
        var shortened = TabTitle.Shorten("Quarterly Report Archive");

        Assert.IsFalse(shortened.Contains(" …"), $"'{shortened}' has a gap before the ellipsis");
    }

    [TestMethod]
    public void NullTitle_IsAnEmptyLabel_NotACrash()
        => Assert.AreEqual(string.Empty, TabTitle.Shorten(null));

    // ── When the full name is worth offering ──────────────────────────────────

    [TestMethod]
    public void IsShortened_IsTrueOnlyWhenSomethingWasDropped()
    {
        Assert.IsFalse(TabTitle.IsShortened("Documents"));
        Assert.IsFalse(TabTitle.IsShortened(new string('x', TabTitle.MaxLength)));
        Assert.IsTrue(TabTitle.IsShortened(new string('x', TabTitle.MaxLength + 1)));
        Assert.IsFalse(TabTitle.IsShortened(null));
    }

    // ── The bindings the strip uses ───────────────────────────────────────────

    private static object? Convert(System.Windows.Data.IValueConverter c, object? v)
        => c.Convert(v!, typeof(string), null!, CultureInfo.InvariantCulture);

    [TestMethod]
    public void LabelBinding_ShowsTheShortenedName()
    {
        var shown = Convert(new TabTitleConverter(), "ProjectAlphaReportsQ4") as string;

        Assert.AreEqual(TabTitle.Shorten("ProjectAlphaReportsQ4"), shown);
    }

    [TestMethod]
    public void TooltipBinding_GivesTheFullNameWhenShortened()
    {
        const string full = "ProjectAlphaReportsQ4";

        Assert.AreEqual(full, Convert(new TabTitleTooltipConverter(), full));
    }

    [TestMethod]
    public void TooltipBinding_IsNullWhenNothingWasHidden()
    {
        // A tooltip that repeats the visible label is noise; a null ToolTip means WPF shows none at all.
        Assert.IsNull(Convert(new TabTitleTooltipConverter(), "Documents"));
        Assert.IsNull(Convert(new TabTitleTooltipConverter(), null));
    }
}
