using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Core.Unit.Markdown;

/// <summary>
/// The heading resolution behind the Markdown tab's scroll-to-heading deep link
/// (<see cref="MarkdownBlocks.FindHeadingBlock"/>): a snaplink carries a '&gt;'-joined heading path and the
/// editor scrolls to the block that path names. Matching is on the whole ancestor hierarchy, so repeated
/// heading names under different parents stay distinct — the case a document-wide text match gets wrong.
/// WPF-free; the scroll itself is one <c>ScrollToVerticalOffset</c> on the resolved block.
/// </summary>
[TestClass]
[CoversNode("markdown-heading-scroll")]
public class MarkdownHeadingPathTests
{
    private static readonly List<string> Doc = MarkdownBlocks.Split(
        """
        # Guide

        Intro text.

        ## Setup

        Install it.

        ### Overview

        Setup overview.

        ## Usage

        ### Overview

        Usage overview.
        """);

    [TestMethod]
    public void FindsATopLevelHeading()
        => Assert.AreEqual("# Guide", Doc[MarkdownBlocks.FindHeadingBlock(Doc, ["Guide"])]);

    [TestMethod]
    public void FindsANestedHeading_ByItsFullPath()
        => Assert.AreEqual("## Setup", Doc[MarkdownBlocks.FindHeadingBlock(Doc, ["Guide", "Setup"])]);

    [TestMethod]
    public void DuplicateHeadingNames_ResolveByTheirParent()
    {
        int underSetup = MarkdownBlocks.FindHeadingBlock(Doc, ["Guide", "Setup", "Overview"]);
        int underUsage = MarkdownBlocks.FindHeadingBlock(Doc, ["Guide", "Usage", "Overview"]);

        Assert.AreNotEqual(underSetup, underUsage, "Both 'Overview' headings resolved to the same block.");
        Assert.AreEqual("Setup overview.", Doc[underSetup + 1]);
        Assert.AreEqual("Usage overview.", Doc[underUsage + 1]);
    }

    [TestMethod]
    public void MatchIsCaseAndWhitespaceInsensitive()
        => Assert.AreEqual(MarkdownBlocks.FindHeadingBlock(Doc, ["Guide", "Setup"]),
                           MarkdownBlocks.FindHeadingBlock(Doc, [" guide ", "SETUP"]));

    [TestMethod]
    public void PartialPath_DoesNotMatch()
        => Assert.AreEqual(-1, MarkdownBlocks.FindHeadingBlock(Doc, ["Setup"]),
                           "A leaf name alone must not match a heading that has ancestors.");

    [TestMethod]
    public void UnknownHeading_ReturnsMinusOne()
        => Assert.AreEqual(-1, MarkdownBlocks.FindHeadingBlock(Doc, ["Guide", "Nope"]));

    [TestMethod]
    public void EmptyOrNullPath_ReturnsMinusOne()
    {
        Assert.AreEqual(-1, MarkdownBlocks.FindHeadingBlock(Doc, null));
        Assert.AreEqual(-1, MarkdownBlocks.FindHeadingBlock(Doc, []));
    }

    [TestMethod]
    public void ClosedAtxHeading_IsMatchedOnItsText()
    {
        var doc = MarkdownBlocks.Split("## Closed ##\n\nbody");

        Assert.AreEqual(0, MarkdownBlocks.FindHeadingBlock(doc, ["Closed"]));
    }
}
