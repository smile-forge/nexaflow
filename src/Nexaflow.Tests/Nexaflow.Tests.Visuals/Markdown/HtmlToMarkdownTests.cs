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

    // ── Email bodies: tables that lay a page out, and what a reader never sees ──

    [TestMethod]
    public void TablesNestedToLayAPageOut_ReadTheirWordsOnce()
    {
        var html = "<table><tr><td><table><tr><td><table role=\"presentation\"><tr><td>"
                 + "<p>We have updated our policy.</p></td></tr></table></td></tr></table></td></tr></table>";

        var md = Convert(html);

        Assert.AreEqual("We have updated our policy.", md, "once, and with no table drawn round it");
    }

    [TestMethod]
    public void ALayoutRowIsItsCellsInOrder_EachOnce()
    {
        var html = "<table><tr><td>Left</td><td><table><tr><td>Inner</td></tr></table></td><td>Right</td></tr></table>";

        var md = Convert(html);

        Assert.AreEqual("Left\n\nInner\n\nRight", md);
    }

    [TestMethod]
    public void ATableOfData_IsStillAMarkdownTable_WithItsOwnRowsOnly()
    {
        var html = "<table><tr><th>Item</th><th>Price</th></tr><tr><td><p>Tea</p></td><td>2 | 3</td></tr></table>";

        var md = Convert(html);

        Assert.AreEqual("| Item | Price |\n| --- | --- |\n| Tea | 2 \\| 3 |", md);
    }

    [TestMethod]
    public void ATableThatSaysItIsPresentation_IsLaidOutEvenInRowsAndColumns()
    {
        var html = "<table role=\"presentation\"><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>";

        StringAssert.DoesNotMatch(Convert(html), new System.Text.RegularExpressions.Regex(@"\|"));
    }

    [TestMethod]
    public void WhatAReaderNeverSees_IsNotConverted()
    {
        var html = "<head><title>Subject</title><style>p { color: red }</style></head><body>"
                 + "<div style=\"display: none !important;\">Preview line</div>"
                 + "<div style=\"max-height:0; overflow:hidden\">Also preview</div>"
                 + "<span style=\"visibility:hidden\">Spacer</span>"
                 + "<div style=\"mso-hide: all\">Outlook only</div>"
                 + "<p hidden>Hidden</p>"
                 + "<script>track()</script>"
                 + "<p>Shown</p></body>";

        Assert.AreEqual("Shown", Convert(html));
    }

    [TestMethod]
    public void APixelImage_IsNotAnImage_ButAPictureIs()
    {
        var html = "<p><img src=\"https://t.example/open.gif\" width=\"1\" height=\"1\">"
                 + "<img src=\"https://t.example/b.gif\" style=\"width:1px;height:1px\">"
                 + "<img src=\"logo.png\" alt=\"Logo\" width=\"120\" height=\"40\"></p>";

        Assert.AreEqual("![Logo|120x40](logo.png)", Convert(html));
    }

    [TestMethod]
    public void InvisiblePadding_IsDropped_ButAJoinerInsideAWordStays()
    {
        var padded = Convert("<p>Your order &zwnj;&nbsp;&zwnj;&nbsp;&#847;&nbsp;&zwnj; has shipped</p>");
        Assert.AreEqual("Your order has shipped", padded);

        // In Persian a non-joiner inside a word is part of how it is spelled.
        const string Word = "\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645";
        Assert.AreEqual(Word, Convert($"<p>{Word}</p>"));
    }

    [TestMethod]
    public void AnInlineElementRoundBlocks_KeepsTheBreaksBetweenThem()
    {
        Assert.AreEqual("One\n\nTwo", Convert("<span><p>One</p><p>Two</p></span>"));
    }

    [TestMethod]
    public void ALinkRoundBlocks_IsStillOneLink()
    {
        Assert.AreEqual("[Shop now](https://shop.example)",
            Convert("<a href=\"https://shop.example\"><table><tr><td><p>Shop now</p></td></tr></table></a>"));
    }

    [TestMethod]
    public void AnImagesDeclaredSize_IsWrittenAfterABar()
    {
        Assert.AreEqual("![Chart|320](a.png)", Convert("<img src=\"a.png\" alt=\"Chart\" width=\"320\">"));
        Assert.AreEqual("![Chart|200x100](a.png)", Convert("<img src=\"a.png\" alt=\"Chart\" width=\"50\" style=\"width:200px; height:100px\">"));
        Assert.AreEqual("![Chart](a.png)", Convert("<img src=\"a.png\" alt=\"Chart\" width=\"100%\">"), "a share of the page is no size of the picture's");
    }

    [TestMethod]
    public void WordsThatWouldReadAsASize_HaveTheirBarEscaped()
    {
        Assert.AreEqual(@"![Page 1\|2](a.png)", Convert("<img src=\"a.png\" alt=\"Page 1|2\">"));
    }
}
