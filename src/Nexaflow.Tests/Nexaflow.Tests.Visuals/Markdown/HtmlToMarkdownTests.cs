using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Tests for HTML→markdown conversion used when pasting / dropping rich content
/// onto the scratchpad. WPF-free (HtmlAgilityPack + string building).
/// </summary>
[TestClass]
[CoversNode("markdown-paste")]
public class HtmlToMarkdownTests
{
    private static string Convert(string html) => HtmlToMarkdown.Convert(html).Trim();

    [TestMethod]
    public void Bold_BecomesAsterisks()
        => Assert.AreEqual("**bold**", Convert("<b>bold</b>"));

    [TestMethod]
    public void Strong_BecomesAsterisks()
        => Assert.AreEqual("**bold**", Convert("<strong>bold</strong>"));

    [TestMethod]
    public void Italic_BecomesSingleAsterisks()
        => Assert.AreEqual("*x*", Convert("<i>x</i>"));

    [TestMethod]
    public void Link_BecomesMarkdownLink()
        => Assert.AreEqual("[link](http://example.com)", Convert("<a href=\"http://example.com\">link</a>"));

    [TestMethod]
    public void Heading_BecomesHashes()
        => Assert.AreEqual("## Title", Convert("<h2>Title</h2>"));

    [TestMethod]
    public void InlineCode_BecomesBackticks()
        => Assert.AreEqual("`code`", Convert("<code>code</code>"));

    [TestMethod]
    public void UnorderedList_BecomesDashes()
        => Assert.AreEqual("- a\n- b", Convert("<ul><li>a</li><li>b</li></ul>"));

    [TestMethod]
    public void OrderedList_BecomesNumbers()
        => Assert.AreEqual("1. a\n2. b", Convert("<ol><li>a</li><li>b</li></ol>"));

    [TestMethod]
    public void Paragraphs_SeparatedByBlankLine()
        => Assert.AreEqual("one\n\ntwo", Convert("<p>one</p><p>two</p>"));

    [TestMethod]
    public void HtmlEntities_AreDecoded()
        => Assert.AreEqual("a & b", Convert("<p>a &amp; b</p>"));

    [TestMethod]
    public void Empty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, HtmlToMarkdown.Convert(""));
        Assert.AreEqual(string.Empty, HtmlToMarkdown.Convert(null));
    }

    [TestMethod]
    public void ConvertClipboardHtml_StripsCfHtmlHeader()
    {
        const string cf =
            "Version:0.9\r\nStartHTML:00000097\r\nEndHTML:00000200\r\n" +
            "StartFragment:00000131\r\nEndFragment:00000164\r\n" +
            "<html><body><!--StartFragment--><b>hi</b><!--EndFragment--></body></html>";
        Assert.AreEqual("**hi**", HtmlToMarkdown.ConvertClipboardHtml(cf).Trim());
    }
}
