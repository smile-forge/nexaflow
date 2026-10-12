using Nexaflow.Markdown.Rtf;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Rtf;

/// <summary>
/// The HTML a mail client wrapped in RTF, taken back out as MS-OXRTFEX says: what is in a tag group copied, the text
/// between the tags copied where <c>\htmlrtf</c> does not mark it off, and RTF that was never HTML left alone.
/// </summary>
[TestClass]
[CoversNode("rtf-html-unwrap")]
public class RtfHtmlUnwrapperTests
{
    [TestMethod]
    public void RtfThatWasNeverHtmlIsNotUnwrapped()
    {
        Assert.IsNull(RtfHtmlUnwrapper.Unwrap(@"{\rtf1\ansi Hello\par}"));
        Assert.IsNull(RtfHtmlUnwrapper.Unwrap(@"{\rtf1\ansi\fromtext Hello\par}"));
        Assert.IsNull(RtfHtmlUnwrapper.Unwrap(@"{\rtf1\ansi\fromhtml0 Hello\par}"));
        Assert.IsNull(RtfHtmlUnwrapper.Unwrap("not rtf at all"));
        Assert.IsNull(RtfHtmlUnwrapper.Unwrap(null));
    }

    [TestMethod]
    public void TheHeaderIsReadOnlyAsFarAsItsFirstTenTokens()
    {
        Assert.IsNull(RtfHtmlUnwrapper.Unwrap(@"{\rtf1\a\b\c\d\e\f\g\h\i\fromhtml1 x}"));
        Assert.IsNull(RtfHtmlUnwrapper.Unwrap(@"{\rtf1 text\fromhtml1 x}"));
    }

    [TestMethod]
    public void TagsAreCopied_AndTheTextBetweenThem_AndWhatHtmlrtfMarksOffIsNot()
    {
        var html = RtfHtmlUnwrapper.Unwrap(
            @"{\rtf1\ansi\fromhtml1 {\*\htmltag1 <p>}Hello\htmlrtf  added for RTF\htmlrtf0 {\*\htmltag2 </p>}\par}");

        Assert.AreEqual("<p>Hello</p>\r\n", html);
    }

    [TestMethod]
    public void HtmlrtfEndsWithTheGroupItWasWrittenIn()
    {
        var html = RtfHtmlUnwrapper.Unwrap(@"{\rtf1\fromhtml1 {\htmlrtf hidden}shown}");

        Assert.AreEqual("shown", html);
    }

    [TestMethod]
    public void WhatEveryOtherGroupHoldsIsReadPast()
    {
        var html = RtfHtmlUnwrapper.Unwrap(
            @"{\rtf1\fromhtml1{\fonttbl{\f0 Arial;}}{\colortbl\red1\green2\blue3;}{\*\generator Me;}{\*\htmltag3 <b>}x}");

        Assert.AreEqual("<b>x", html);
    }

    [TestMethod]
    public void ParagraphsAndLinesAreLineEndings_TabsAreTabs()
    {
        Assert.AreEqual("a\r\nb\r\nc\td", RtfHtmlUnwrapper.Unwrap(@"{\rtf1\fromhtml1 a\par b\line c\tab d}"));
    }

    [TestMethod]
    public void EscapesAreTheCharactersTheyStandFor()
    {
        Assert.AreEqual("<p>{x}\\</p>", RtfHtmlUnwrapper.Unwrap(@"{\rtf1\fromhtml1 {\*\htmltag1 <p>\{x\}\\</p>}}"));
        Assert.AreEqual("a" + (char)0x2014 + "b", RtfHtmlUnwrapper.Unwrap(@"{\rtf1\fromhtml1\uc1 a\u8212?b}"));
    }

    [TestMethod]
    public void ATagsBytesAreInTheDocumentsCodePage_TheBodysInItsFonts()
    {
        var html = RtfHtmlUnwrapper.Unwrap(
            @"{\rtf1\ansi\ansicpg1252\fromhtml1{\fonttbl{\f1\fcharset204 Arial Cyr;}}{\*\htmltag1 <p title=""caf\'e9"">}\f1 \'e9}");

        Assert.AreEqual("<p title=\"caf" + (char)0xE9 + "\">" + (char)0x439, html);
    }

    [TestMethod]
    public void TheCurrentFontIsFollowedEvenWhereItIsMarkedOff()
    {
        var html = RtfHtmlUnwrapper.Unwrap(
            @"{\rtf1\ansi\fromhtml1{\fonttbl{\f1\fcharset204 Arial Cyr;}}\htmlrtf\f1\htmlrtf0 \'e9}");

        Assert.AreEqual(((char)0x439).ToString(), html);
    }

    [TestMethod]
    public void AnOutlookBodyComesBackAsItsHtml()
    {
        const string rtf = @"{\rtf1\ansi\ansicpg1252\fromhtml1 \fbidis \deff0{\fonttbl" + "\r\n" +
                           @"{\f0\fswiss\fcharset0 Arial;}" + "\r\n" +
                           @"{\f1\fmodern Courier New;}}" + "\r\n" +
                           @"{\colortbl\red0\green0\blue0;\red0\green0\blue255;}" + "\r\n" +
                           @"\uc1\pard\plain\deftab360 \f0\fs20 {\*\htmltag19 <html>}" + "\r\n" +
                           @"{\*\htmltag50 <body>}\htmlrtf {\htmlrtf0 " + "\r\n" +
                           @"{\*\htmltag64 <p>}" + "\r\n" +
                           @"{\*\htmltag84 <a href=""http://example.com"">}\htmlrtf {\field{\*\fldinst{HYPERLINK ""http://example.com""}}{\fldrslt\cf1\ul \htmlrtf0 Example\htmlrtf }\htmlrtf0 \htmlrtf }\htmlrtf0 " + "\r\n" +
                           @"{\*\htmltag92 </a>}" + "\r\n" +
                           @"{\*\htmltag244 </p>}" + "\r\n" +
                           @"{\*\htmltag58 </body>}" + "\r\n" +
                           @"{\*\htmltag27 </html>}}}";

        Assert.AreEqual("<html><body><p><a href=\"http://example.com\">Example</a></p></body></html>", RtfHtmlUnwrapper.Unwrap(rtf));
    }
}
